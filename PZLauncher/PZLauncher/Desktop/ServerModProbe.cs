using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace PZLauncher.Desktop;

internal sealed record ServerModEntry(string Id, string WorkshopId, string Name);
internal sealed record ServerWorkshopEntry(string Id, long Updated);
internal sealed class ServerModCapture
{
    public string GameVersion { get; set; } = "";
    public string Map { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
    public List<ServerModEntry> Mods { get; set; } = [];
    public List<ServerWorkshopEntry> Workshop { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public List<string> WorkshopIds => Workshop.Select(w => w.Id).Concat(Mods.Select(m => m.WorkshopId))
        .Where(WorkshopStore.ValidId).Distinct().ToList();
    internal void Validate()
    {
        if (Mods == null || Workshop == null || Mods.Count > 2000 || Workshop.Count > 2000 ||
            Mods.Any(m => m == null || string.IsNullOrWhiteSpace(m.Id) || m.Id.Length > 256 || m.Id.Contains(';') || m.Id.Any(char.IsControl) ||
                m.Name == null || m.Name.Length > 8192 || m.WorkshopId == null || m.WorkshopId.Length > 0 && !WorkshopStore.ValidId(m.WorkshopId)) ||
            Workshop.Any(w => w == null || !WorkshopStore.ValidId(w.Id) || w.Updated < 0) ||
            Mods.Select(m => m.Id.TrimStart('\\')).Distinct().Count() != Mods.Count ||
            Workshop.Select(w => w.Id).Distinct().Count() != Workshop.Count || string.IsNullOrEmpty(GameVersion))
            throw new InvalidDataException(T("probe.invalid"));
    }
}

internal sealed record ServerProbeCredentials(string User, string Password, string ServerPassword, string Code, bool Steam, bool Relay);
internal static class ServerModProbe
{
    internal static async Task<ServerModCapture> RunAsync(string installation, OnlineServerInfo server, ServerProbeCredentials credentials,
        CancellationToken cancellation, IProgress<string>? progress = null)
    {
        OnlineProfiles.Host(server.Host);
        if (server.Port is < 1 or > 65535 || credentials.User.Trim().Length is 0 or > 64 || credentials.User.Any(char.IsControl))
            throw new InvalidDataException(T("probe.userRequired"));
        string temporary = Path.Combine(Path.GetTempPath(), "PZLauncher-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string jar = Path.Combine(temporary, "probe.jar");
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("PZLauncher.Resources.Java.pz-server-probe.jar")
                ?? throw new FileNotFoundException(T("java.agentMissing")))
            using (var file = File.Create(jar)) await resource.CopyToAsync(file, cancellation);
            using var process = new Process { StartInfo = StartInfo(installation, jar, temporary, credentials.Steam) };
            if (!process.Start()) throw new IOException(T("error.start"));
            string error = ""; bool completed = false;
            async Task ReadOutput()
            {
                while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (!line.StartsWith("PZPROBE|", StringComparison.Ordinal)) continue;
                    string state = line[8..];
                    if (state.StartsWith("ERROR:")) error = state[6..];
                    else if (state == "COMPLETE") completed = true;
                    else if (state.StartsWith("RECEIVING:")) progress?.Report(T("probe.receiving", state[10..]));
                    else if (state is "CONNECTING" or "AUTHENTICATING" || state.StartsWith("READY:"))
                        progress?.Report(T(state == "CONNECTING" ? "probe.connecting" : state == "AUTHENTICATING" ? "probe.authenticating" : "probe.ready"));
                }
            }
            async Task DrainError() { while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) != null) { } }
            var output = ReadOutput(); var stderr = DrainError();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(80));
            try
            {
                foreach (string value in new[] { server.Host, server.Port.ToString(), credentials.Steam ? "1" : "0", credentials.Relay ? "1" : "0",
                    credentials.User, credentials.Password, credentials.ServerPassword, credentials.Code })
                {
                    if (Encoding.UTF8.GetByteCount(value) > 8192) throw new InvalidDataException(T("probe.invalid"));
                    await process.StandardInput.WriteLineAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).AsMemory(), timeout.Token);
                }
                await process.StandardInput.FlushAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(output, stderr);
                cancellation.ThrowIfCancellationRequested();
                if (!completed || process.ExitCode != 0)
                {
                    foreach (string secret in new[] { credentials.Password, credentials.ServerPassword, credentials.Code }.Where(s => s.Length > 0))
                        error = error.Replace(secret, "***", StringComparison.Ordinal);
                    throw new IOException(Error(error));
                }
                string path = Path.Combine(temporary, "mods.json");
                if (!File.Exists(path) || new FileInfo(path).Length > 8_000_000) throw new InvalidDataException(T("probe.invalid"));
                var result = JsonSerializer.Deserialize<ServerModCapture>(await File.ReadAllTextAsync(path, cancellation))
                    ?? throw new InvalidDataException(T("probe.invalid"));
                result.Validate(); result.CapturedAt = DateTimeOffset.UtcNow;
                return result;
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new IOException(T("probe.timeout")); }
            finally
            {
                if (!process.HasExited)
                {
                    try { await process.StandardInput.WriteLineAsync("cancel"); await process.StandardInput.FlushAsync(); } catch (IOException) { }
                    using var grace = new CancellationTokenSource(2000);
                    try { await process.WaitForExitAsync(grace.Token); }
                    catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); }
                }
                await Task.WhenAll(output, stderr);
            }
        }
        finally { try { Directory.Delete(temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    internal static ProcessStartInfo StartInfo(string installation, string jar, string directory, bool steam)
    {
        if (!InstallationLocator.IsValid(installation)) throw new InvalidDataException(T("install.invalid"));
        var version = InstallationLocator.ReadGameVersion(installation);
        if (version is not { Major: 42, Minor: 20 }) throw new InvalidOperationException(T("probe.version", version?.ToString() ?? "?"));
        if (steam && InstallationLocator.IsGog(installation)) throw new InvalidOperationException(T("probe.steam"));
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(installation, "ProjectZomboid64.json")));
        var cp = config.RootElement.GetProperty("classpath").EnumerateArray().Select(e => Path.GetFullPath(e.GetString()!, installation));
        var start = new ProcessStartInfo(Path.Combine(installation, "jre64", "bin", "java.exe"))
        {
            WorkingDirectory = installation, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in new[] { "-Xmx512m", "-Djava.awt.headless=true", "--enable-native-access=ALL-UNNAMED",
            "--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED", "-XX:-CreateCoredumpOnCrash",
            "-Djava.library.path=" + Path.Combine(installation, "win64") + Path.PathSeparator + installation,
            "-Duser.home=" + directory, "-javaagent:" + jar, "-cp", string.Join(Path.PathSeparator, new[] { jar }.Concat(cp)),
            "community.pzprobe.Probe", directory }) start.ArgumentList.Add(arg);
        start.Environment["SteamAppId"] = "108600"; start.Environment["SteamGameId"] = "108600";
        return start;
    }
    private static string Error(string code) => code switch
    {
        "OTP" => T("probe.otpRequired"), "TIMEOUT" => T("probe.timeout"), "STEAM" => T("probe.steam"),
        "DATA" => T("probe.invalid"), "CONNECTION:24" => T("probe.badServerPassword"),
        _ when code.StartsWith("VERSION:") => T("probe.version", code[8..]),
        _ when code.StartsWith("DENIED:") => T("probe.denied", code[7..]),
        _ => T("probe.failed", code.Length == 0 ? "runtime" : code)
    };
}
