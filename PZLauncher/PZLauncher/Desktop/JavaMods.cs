using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal sealed record JavaModEntry(string Path, string Id, string Name, string Version, string Owner, string Issue, InstalledMod Metadata)
{
    public bool Available => Issue.Length == 0;
    public string Loader { get; init; } = JavaMods.Community;
    public bool OwnerEnabled { get; init; } = true;
    public bool DeclarationOnly { get; init; }
    public string Side { get; init; } = "unspecified";
}
internal sealed record JavaLaunch(string AgentPath, string ManifestPath, List<string> Ordered)
{
    public string Loader { get; init; } = JavaMods.Community;
    public List<string> Classpath { get; init; } = [];
    public List<string> VmArguments { get; init; } = [];
    public string MainClass { get; init; } = "";
    public string Report { get; init; } = "";
    public bool LooseClassesFirst { get; init; }
}

internal static partial class JavaMods
{
    internal const string Descriptor = "META-INF/pz-java-mod.properties";
    internal static string Folder(PlayerProfile profile) => Path.Combine(profile.CachePath, "java-mods");
    private static JavaModEntry InspectCommunity(string path, string gameVersion, string owner = "", string side = "client")
    {
        string id = Path.GetFileNameWithoutExtension(path), name = id, version = "";
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var descriptor = archive.GetEntry(Descriptor) ?? throw new InvalidDataException(T("java.legacy"));
            if (descriptor.Length > 65536) throw new InvalidDataException(T("java.invalid"));
            using var input = new StreamReader(descriptor.Open(), Encoding.UTF8);
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            while (input.ReadLine() is { } line)
            {
                line = line.Trim(); if (line.Length == 0 || line.StartsWith('#')) continue;
                int split = line.IndexOf('=');
                if (split < 1 || !fields.TryAdd(line[..split].Trim(), line[(split + 1)..].Trim())) throw new InvalidDataException(T("java.invalid"));
            }
            id = fields.GetValueOrDefault("id", ""); name = fields.GetValueOrDefault("name", id); version = fields.GetValueOrDefault("version", "");
            if (!Regex.IsMatch(id, "^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,99}$") || version.Length == 0 || fields.GetValueOrDefault("api") != "1" ||
                !Regex.IsMatch(fields.GetValueOrDefault("entrypoint", ""), "^[A-Za-z_$][A-Za-z0-9_$]*(\\.[A-Za-z_$][A-Za-z0-9_$]*)+$") ||
                fields.GetValueOrDefault("side") is not ("client" or "server" or "both")) throw new InvalidDataException(T("java.invalid"));
            if (fields["side"] != side && fields["side"] != "both")
                return new(path, id, name, version, owner, T("java.sideMismatch", fields["side"], side), new() { Id = id, Name = name, Available = false }) { Side = fields["side"] };
            if (!ModCatalog.MetadataList(fields, "gameVersions").Contains(gameVersion)) throw new InvalidDataException(T("java.version", gameVersion));
            if (archive.Entries.Any(e => e.FullName.EndsWith(".class") &&
                (e.FullName.StartsWith("zombie/") || e.FullName.StartsWith("community/pzloader/") || e.FullName.StartsWith("META-INF/versions/"))))
                throw new InvalidDataException(T("java.legacy"));
            if (owner == "@legacy") throw new InvalidDataException(T("java.move"));
            return new(path, id, name, version, owner, "", new()
            {
                Id = id, Name = name, Location = path, Available = true,
                Requires = ModCatalog.MetadataList(fields, "require"), LoadAfter = ModCatalog.MetadataList(fields, "loadModAfter"),
                LoadBefore = ModCatalog.MetadataList(fields, "loadModBefore"), Incompatible = ModCatalog.MetadataList(fields, "incompatible")
            }) { Side = fields["side"] };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        { return new(path, id, name, version, owner, e.Message, new() { Id = id, Name = name, Available = false }); }
    }
    private static JavaLaunch PrepareCommunity(PlayerProfile profile, IReadOnlyList<JavaModEntry> catalog, string installation, string side)
    {
        var hardware = HardwareAdvisor.Detect(installation);
        if (!hardware.JavaVersion.StartsWith("25.")) throw new InvalidOperationException(T("java.runtime"));
        var resolved = ModResolver.Resolve(catalog.Select(m => m.Metadata), Requested(profile, catalog));
        if (!resolved.Success) throw new InvalidOperationException(string.Join("\n", resolved.Issues));
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("PZLauncher.Resources.Java.pz-loader.jar")
            ?? throw new FileNotFoundException(T("java.agentMissing"));
        using var memory = new MemoryStream(); resource.CopyTo(memory); byte[] bytes = memory.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string directory = Path.Combine(LauncherStorage.Root, "runtime", hash);
        Directory.CreateDirectory(directory);
        string agent = Path.Combine(directory, "pz-loader.jar");
        if (!File.Exists(agent)) File.WriteAllBytes(agent, bytes);
        else if (!File.ReadAllBytes(agent).AsSpan().SequenceEqual(bytes)) throw new IOException(T("java.agentChanged"));
        string B64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(value)));
        var lines = new List<string> { "format=1", "side=" + side, "gameVersion=" + InstallationLocator.ReadGameVersion(installation),
            "gameJar=" + B64(Path.Combine(installation, "projectzomboid.jar")), "modCount=" + resolved.Ordered.Count };
        for (int i = 0; i < resolved.Ordered.Count; i++)
        {
            var mod = catalog.Single(m => m.Id == resolved.Ordered[i] && m.Available);
            using var file = File.OpenRead(mod.Path);
            lines.Add($"mod.{i}.path=" + B64(mod.Path));
            lines.Add($"mod.{i}.sha256=" + Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant());
        }
        string manifest = Path.Combine(LauncherStorage.Root, "runtime", "profiles", profile.Id, "launch.properties");
        LauncherStorage.WriteAtomic(manifest, string.Join("\n", lines) + "\n");
        return new(agent, manifest, resolved.Ordered);
    }
    internal static LaunchPlan Attach(LaunchPlan plan, JavaLaunch java)
    {
        var args = plan.Arguments.ToList();
        
        args.RemoveAll(a => a.StartsWith("-javaagent:") || a.StartsWith("-agentlib:") || a.StartsWith("-agentpath:") || a.StartsWith("-Dleaf."));
        foreach (string property in java.VmArguments.Where(a => a.StartsWith("-D") && a.Contains('=')))
            args.RemoveAll(a => a.StartsWith(property[..(property.IndexOf('=') + 1)], StringComparison.Ordinal));
        int cp = args.IndexOf("-cp");
        if (cp < 0 || cp + 2 >= args.Count) throw new InvalidDataException(T("error.mainClass"));
        var originalClasspath = args[cp + 1].Split(Path.PathSeparator).Where(p => !p.Replace('\\', '/').Contains(".leaf/lib/", StringComparison.OrdinalIgnoreCase)).ToList();
        bool Local(string path) => Path.GetFullPath(path, plan.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(plan.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        var classpath = java.Classpath.Concat(originalClasspath);
        if (java.LooseClassesFirst) classpath = originalClasspath.Where(Local).Concat(java.Classpath).Concat(originalClasspath.Where(p => !Local(p)));
        args[cp + 1] = string.Join(Path.PathSeparator, classpath.Distinct(StringComparer.OrdinalIgnoreCase));
        if (java.MainClass.Length > 0) args[cp + 2] = java.MainClass;
        else if (args[cp + 2].StartsWith("dev.aoqia.leaf.")) args[cp + 2] = args[cp + 2].EndsWith("KnotServer") ? "zombie.network.GameServer" : "zombie.gameStates.MainScreenState";
        if (java.AgentPath.Length > 0) args.Insert(0, "-javaagent:" + java.AgentPath + (java.ManifestPath.Length > 0 ? "=" + java.ManifestPath : ""));
        args.InsertRange(0, java.VmArguments);
        return new() { Arguments = args, JavaExecutable = plan.JavaExecutable, WorkingDirectory = plan.WorkingDirectory,
            CacheDirectory = plan.CacheDirectory, ConsoleLogPath = plan.ConsoleLogPath };
    }
    internal static async Task<string> PreflightAsync(LaunchPlan plan, JavaLaunch java)
    {
        if (java.Loader != Community) { LauncherStorage.Log(java.Report); return java.Report; }
        int cp = plan.Arguments.ToList().IndexOf("-cp");
        var start = new ProcessStartInfo(plan.JavaExecutable) { WorkingDirectory = plan.WorkingDirectory, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("_JAVA_OPTIONS");
        foreach (string arg in new[] { "-Xmx512m", "-Xverify:all", "-cp", java.AgentPath + Path.PathSeparator + plan.Arguments[cp + 1], "community.pzloader.Loader", java.ManifestPath })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException(T("error.start"));
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false); throw new IOException(T("java.timeout")); }
        string report = (await output.ConfigureAwait(false)) + (await error.ConfigureAwait(false));
        LauncherStorage.Log(report);
        if (process.ExitCode != 0) throw new InvalidOperationException(T("java.failed") + "\n" + report);
        return report;
    }
}
