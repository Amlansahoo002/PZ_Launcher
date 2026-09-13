using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal sealed record ConfigChoice(string Value, string Label) { public override string ToString() => Label; }
internal sealed class ConfigOption
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";
    public string Default { get; set; } = "";
    public double? Min { get; set; }
    public double? Max { get; set; }
    public int? MaxLength { get; set; }
    public string? Title { get; set; }
    public string? Tooltip { get; set; }
    public List<ConfigChoice> Choices { get; set; } = [];
    internal void Validate(string value)
    {
        bool valid = !value.Contains('\0') && (MaxLength is not > 0 || value.Length <= MaxLength);
        if (Type == "boolean") valid &= value is "true" or "false";
        if (Type is "integer" or "double" or "enum")
        {
            valid &= double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n)
                && (!Min.HasValue || n >= Min) && (!Max.HasValue || n <= Max) && (Type == "double" || n == Math.Truncate(n));
        }
        if (Choices.Count > 0) valid &= Choices.Any(c => c.Value == value);
        if (!valid) throw new InvalidDataException(T("config.invalid", Name, Min?.ToString(CultureInfo.InvariantCulture) ?? "…", Max?.ToString(CultureInfo.InvariantCulture) ?? "…"));
    }
    internal static ConfigOption Infer(string name, string value) => new() { Name = name, Default = value, Type = value is "true" or "false" ? "boolean" : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? "double" : "string" };
}
internal sealed class GameConfigurationSchema
{
    public List<ConfigOption> Server { get; set; } = [];
    public List<ConfigOption> Sandbox { get; set; } = [];
    public int SandboxVersion { get; set; }
    private static readonly SemaphoreSlim ProbeLock = new(1);
    internal static async Task<GameConfigurationSchema> Load(string installation)
    {
        await ProbeLock.WaitAsync();
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var resource = assembly.GetManifestResourceStream("PZLauncher.Resources.Java.pz-config-inspector.jar") ?? throw new IOException("Missing config inspector");
            using var memory = new MemoryStream(); resource.CopyTo(memory); byte[] jar = memory.ToArray();
            string identity = Path.GetFullPath(installation) + Convert.ToHexString(SHA256.HashData(jar));
            foreach (string path in new[] { "projectzomboid.jar", "zombie/network/ServerOptions.class", "java/zombie/network/ServerOptions.class", "jre64/release" })
            { var info = new FileInfo(Path.Combine(installation, path)); identity += info.Exists ? $"|{path}:{info.Length}:{info.LastWriteTimeUtc.Ticks}" : ""; }
            string key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..24];
            string directory = Path.Combine(LauncherStorage.Root, "tools", "config-inspector", key); Directory.CreateDirectory(directory);
            string output = Path.Combine(directory, "options.json");
            if (!File.Exists(output))
            {
                string tool = Path.Combine(directory, "inspector.jar"); File.WriteAllBytes(tool, jar);
                string cache = Path.Combine(directory, "isolated-cache"); Directory.CreateDirectory(cache);
                var p = new PlayerProfile { CachePath = cache, Steam = false };
                var plan = new GameLaunchService().CreateLaunchPlan(installation, p.AsGameProfile(), p.AsJvmProfile());
                int cpIndex = plan.Arguments.ToList().IndexOf("-cp");
                string cp = plan.Arguments[cpIndex + 1];
                
                if (File.Exists(Path.Combine(installation, "projectzomboid.jar"))) cp = "projectzomboid.jar";
                var start = new ProcessStartInfo(plan.JavaExecutable) { WorkingDirectory = installation, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string arg in new[] { "-Xmx512m", "-Djava.awt.headless=true", "-Djava.library.path=win64/;.", "-cp", tool + ";" + cp, "community.pzconfig.Inspector", output, cache }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new IOException("Config inspector did not start");
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch { if (!process.HasExited) process.Kill(true); throw; }
                string log = await stdout + "\n" + await stderr; File.WriteAllText(Path.Combine(directory, "probe.log"), log);
                if (process.ExitCode != 0 || !File.Exists(output)) throw new IOException(T("config.probeFailed", Path.Combine(directory, "probe.log")));
            }
            var schema = JsonSerializer.Deserialize<GameConfigurationSchema>(File.ReadAllText(output)) ?? throw new InvalidDataException("Config schema");
            if (schema.Server.Count == 0 || schema.Sandbox.Count == 0) throw new InvalidDataException("Empty config schema");
            var translations = new Dictionary<string, string>();
            foreach (string language in new[] { "EN", L.Current.GameLanguage }.Distinct())
                foreach (string category in new[] { "Sandbox", "UI" })
                {
                    string folder = Path.Combine(installation, "media", "lua", "shared", "Translate", language);
                    string json = Path.Combine(folder, category + ".json");
                    if (File.Exists(json)) foreach (var item in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(json)) ?? []) translations[item.Key] = item.Value;
                    else
                    {
                        string legacy = Path.Combine(folder, category + "_" + language + ".txt");
                        if (File.Exists(legacy)) foreach (Match match in Regex.Matches(File.ReadAllText(legacy), "(?m)^\\s*(\\w+)\\s*=\\s*\"((?:\\\\.|[^\"])*)\"")) translations[match.Groups[1].Value] = match.Groups[2].Value.Replace("\\\"", "\"");
                    }
                }
            string Translate(string? key, string fallback) => key == null ? fallback : translations.GetValueOrDefault(key, key);
            foreach (var option in schema.Server.Concat(schema.Sandbox))
            {
                string? titleKey = option.Title;
                option.Title = Translate(titleKey, option.Name);
                option.Tooltip = Translate(option.Tooltip, translations.GetValueOrDefault("UI_ServerOption_" + option.Name + "_tooltip", translations.GetValueOrDefault(titleKey + "_tooltip", "")));
                option.Tooltip = Regex.Replace(option.Tooltip, "<[^>]+>", " ");
                option.Choices = option.Choices.Select(c => c with { Label = Translate(c.Label, c.Value) }).ToList();
            }
            return schema;
        }
        finally { ProbeLock.Release(); }
    }
}
