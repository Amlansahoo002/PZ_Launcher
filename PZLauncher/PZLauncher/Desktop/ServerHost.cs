using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal sealed class ServerPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public PlayerProfile Java { get; set; } = new() { JavaLoader = JavaMods.None, JavaLoaderEnabled = false };
    public string ClientAddress { get; set; } = "127.0.0.1";
    public string Name { get; set; } = "servertest";
    public string CachePath { get; set; } = LauncherStorage.DefaultGameData;
    public bool Steam { get; set; } = true;
    public int XmsMb { get; set; } = 1024;
    public int XmxMb { get; set; } = 4096;
    public string Collector { get; set; } = "ZGC";
    public int StackKb { get; set; }
    public int PauseTargetMs { get; set; }
    public bool StringDeduplication { get; set; }
    public string RuntimePackSha256 { get; set; } = "";
    public override string ToString() => Name;
    internal string ConfigDirectory => Path.Combine(CachePath, "Server");
    internal string Ini => WorldFiles.Within(ConfigDirectory, Name + ".ini");
    internal string World => WorldFiles.Within(Path.Combine(CachePath, "Saves", "Multiplayer"), Name);
    internal PlayerProfile JavaProfile()
    {
        Java.CachePath = CachePath; Java.Steam = Steam; Java.Name = Name; Java.DirectLooseClassesFirst = false;
        Java.Launch.ModFolders = "workshop,steam,mods"; 
        return Java;
    }
    internal List<string> EnabledMods() => ServerFiles.IniValues(File.Exists(Ini) ? File.ReadAllText(Ini) : "").GetValueOrDefault("Mods", "").Replace("\\", "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    internal void Validate()
    {
        if (!Regex.IsMatch(Name, "^[A-Za-z0-9_-]{1,64}$") || XmsMb < 0 || XmxMb is < 1024 or > 131072 || XmsMb > XmxMb || Collector is not ("G1" or "ZGC") || StackKb != 0 && StackKb is < 256 or > 16384 || PauseTargetMs is < 0 or > 2000)
            throw new InvalidDataException(T("server.invalid"));
    }
}
internal static class ServerFiles
{
    internal static readonly string[] Suffixes = [".ini", "_SandboxVars.lua", "_spawnregions.lua", "_spawnpoints.lua"];
    internal static Dictionary<string,string> Read(ServerPreset preset) => Suffixes.ToDictionary(s => s, s =>
    {
        string path = WorldFiles.Within(preset.ConfigDirectory, preset.Name + s);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    });
    internal static void Create(ServerPreset preset)
    {
        preset.Validate(); if (File.Exists(preset.Ini)) throw new IOException(T("server.exists"));
        var initial = new Dictionary<string,string>
        {
            ["Public"] = "false", ["PublicName"] = preset.Name, ["DefaultPort"] = "16261", ["UDPPort"] = "16262",
            ["MaxPlayers"] = "32", ["PVP"] = "true", ["PauseEmpty"] = "true", ["Open"] = "true",
            ["Password"] = "", ["Mods"] = "", ["WorkshopItems"] = "", ["Map"] = "Muldraugh, KY",
            ["SaveWorldEveryMinutes"] = "10", ["BackupsCount"] = "5", ["BackupsOnStart"] = "true", ["BackupsOnVersionChange"] = "true", ["BackupsPeriod"] = "0"
        };
        new OptionDocument(preset.Ini).SaveChanges(initial);
        
    }
    internal static void Save(ServerPreset preset, Dictionary<string,string> original, Dictionary<string,string> edited)
    {
        preset.Validate(); GameProcessGuard.EnsureStopped(preset.CachePath);
        foreach (string suffix in Suffixes)
        {
            string path = WorldFiles.Within(preset.ConfigDirectory, preset.Name + suffix);
            string current = File.Exists(path) ? File.ReadAllText(path) : "";
            if (current != original[suffix]) throw new IOException(T("world.changed"));
            if (edited[suffix].Length > 4_000_000 || edited[suffix].Contains('\0')) throw new IOException(T("server.invalid"));
        }
        ValidateIni(edited[".ini"]);
        var written = new List<(string Suffix, string Path, bool Existed)>();
        try
        {
            foreach (string suffix in Suffixes.Where(s => edited[s] != original[s]))
            {
                string path = WorldFiles.Within(preset.ConfigDirectory, preset.Name + suffix); bool existed = File.Exists(path);
                LauncherStorage.WriteAtomic(path, edited[suffix]); written.Add((suffix, path, existed));
            }
        }
        catch
        {
            foreach (var item in written.AsEnumerable().Reverse())
                if (item.Existed) LauncherStorage.WriteAtomic(item.Path, original[item.Suffix]); else File.Delete(item.Path);
            throw;
        }
        foreach (var item in written) original[item.Suffix] = edited[item.Suffix];
    }
    internal static Dictionary<string,string> IniValues(string text) => text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith(';') && l.Contains('='))
        .Select(l => l.Split('=', 2)).GroupBy(p => p[0].Trim(), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last()[1].Trim());
    internal static void ValidateIni(string text)
    {
        var values = IniValues(text);
        foreach (string key in new[] { "DefaultPort", "UDPPort", "RCONPort" })
            if (values.TryGetValue(key, out string? value) && (!int.TryParse(value, out int port) || port < 0 || port > 65535))
                throw new IOException(T("server.port", key));
        if (values.TryGetValue("MaxPlayers", out string? max) && (!int.TryParse(max, out int players) || players is < 1 or > 254)) throw new IOException(T("server.players"));
        
        foreach (var (key, minimum, maximum) in new[] { ("BackupsCount", 1, 300), ("BackupsPeriod", 0, 1500), ("SaveWorldEveryMinutes", 0, int.MaxValue) })
            if (values.TryGetValue(key, out string? value) && (!int.TryParse(value, out int number) || number < minimum || number > maximum))
                throw new IOException(T("server.range", key, minimum, maximum));
        foreach (string key in new[] { "Public", "PVP", "PauseEmpty", "Open", "BackupsOnStart", "BackupsOnVersionChange" })
            if (values.TryGetValue(key, out string? value) && !bool.TryParse(value, out _) && value is not ("0" or "1"))
                throw new IOException(T("server.boolean", key));
    }
    internal static string UpdateValue(string text, string key, string value)
    {
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new IOException(T("server.invalid"));
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList(); bool found = false;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Split('=', 2)[0].Trim() == key) { lines[i] = key + "=" + value; found = true; }
        if (!found) lines.Add(key + "=" + value); return string.Join(Environment.NewLine, lines);
    }
    internal static string Backup(ServerPreset preset, string? destination = null)
    {
        GameProcessGuard.EnsureStopped(preset.CachePath);
        string staging = Path.Combine(LauncherStorage.Root, "server-snapshots", Guid.NewGuid().ToString("N"));
        string backupRoot = destination ?? Path.Combine(LauncherStorage.Root, "backups", "servers", preset.Name);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "pzlauncher-server.json"), JsonSerializer.Serialize(preset));
        foreach (string relative in new[] { "mods", "java-mods", ".leaf", ".pzlauncher-loaders" })
            if (Directory.Exists(WorldFiles.Within(preset.CachePath, relative)))
                foreach (string source in WorldFiles.Enumerate(WorldFiles.Within(preset.CachePath, relative)))
                { string target = WorldFiles.Within(staging, Path.GetRelativePath(preset.CachePath, source)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target); }
        foreach (string source in ServerRuntimePack.BackupFiles(preset))
        {
            string target = WorldFiles.Within(staging, Path.GetRelativePath(preset.CachePath, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, true);
        }
        
        foreach (string suffix in Suffixes)
        {
            string source = WorldFiles.Within(preset.ConfigDirectory, preset.Name + suffix);
            if (!File.Exists(source)) continue;
            string target = WorldFiles.Within(staging, "Server/" + preset.Name + suffix);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
        }
        if (Directory.Exists(preset.World))
            foreach (string source in WorldFiles.Enumerate(preset.World))
            {
                if (source.EndsWith(".db-wal") || source.EndsWith(".db-shm") || source.EndsWith(".db-journal")) continue;
                string target = WorldFiles.Within(staging, "Saves/Multiplayer/" + preset.Name + "/" + Path.GetRelativePath(preset.World, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (source.EndsWith(".db")) DatabaseReader.Snapshot(source, target); else File.Copy(source, target);
            }
        string database = WorldFiles.Within(Path.Combine(preset.CachePath, "db"), preset.Name + ".db");
        if (File.Exists(database))
        {
            string target = WorldFiles.Within(staging, "db/" + preset.Name + ".db"); Directory.CreateDirectory(Path.GetDirectoryName(target)!); DatabaseReader.Snapshot(database, target);
        }
        string result = WorldFiles.Backup(staging, backupRoot);
        foreach (string file in WorldFiles.Enumerate(staging)) File.Delete(file);
        
        return result;
    }
    internal static ServerPreset Restore(string archive, string parent)
    {
        ServerPreset preset;
        using (var zip = ZipFile.OpenRead(archive))
        {
            var metadata = zip.GetEntry("pzlauncher-server.json");
            if (metadata == null || metadata.Length > 65536) throw new InvalidDataException(T("server.backupFormat"));
            using var input = metadata.Open(); preset = JsonSerializer.Deserialize<ServerPreset>(input) ?? throw new InvalidDataException(T("server.backupFormat"));
            preset.Validate();
            if (zip.GetEntry("Server/" + preset.Name + ".ini") == null) throw new InvalidDataException(T("server.backupFormat"));
        }
        string previousCache = preset.CachePath;
        preset.CachePath = WorldFiles.RestoreAsCopy(archive, parent); preset.Id = Guid.NewGuid().ToString("N");
        preset.Java.Id = Guid.NewGuid().ToString("N");
        RuntimeClients.Relocate(preset.Java, previousCache, preset.CachePath);
        return preset;
    }
    internal static LaunchPlan Plan(string installation, ServerPreset preset, string adminPassword = "", bool includeJava = true)
    {
        preset.Validate();
        if (!File.Exists(Path.Combine(installation, "projectzomboid.jar"))) throw new FileNotFoundException(T("error.jar"));
        if (adminPassword.IndexOfAny(['\0', '\r', '\n']) >= 0) throw new InvalidDataException(T("server.invalid"));
        bool steam = preset.Steam && !InstallationLocator.IsGog(installation);
        
        var args = new List<string> { "--enable-native-access=ALL-UNNAMED", "--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED",
            "-XX:+Use" + (preset.Collector == "G1" ? "G1GC" : "ZGC"), "-XX:-CreateCoredumpOnCrash", "-XX:-OmitStackTraceInFastThrow", "-Xmx" + preset.XmxMb + "m",
            "-Djava.library.path=./natives/;./natives/win64/;./", "-Dzomboid.steam=" + (steam ? "1" : "0"), "-Dpzlauncher.runtimeSide=server" };
        if (preset.XmsMb > 0) args.Add("-Xms" + preset.XmsMb + "m");
        if (preset.StackKb > 0) args.Add("-Xss" + preset.StackKb + "k");
        if (preset.Collector == "G1" && preset.PauseTargetMs > 0) args.Add("-XX:MaxGCPauseMillis=" + preset.PauseTargetMs);
        if (preset.Collector == "G1" && preset.StringDeduplication) args.Add("-XX:+UseStringDeduplication");
        string classpath = ".;projectzomboid.jar";
        if (preset.RuntimePackSha256.Length > 0)
        {
            var pack = ServerRuntimePack.VerifyInstalled(preset, installation);
            var overlays = pack.JavaClasspath(Path.GetDirectoryName(ServerRuntimePack.Manifest(preset))!, "server");
            
            classpath = string.Join(Path.PathSeparator, overlays.Concat(["projectzomboid.jar", "."]));
            args.Add("-Dpzlauncher.runtimePack=" + pack.Id + "@" + pack.Version);
            args.Add("-Dpzlauncher.runtimePackSha256=" + preset.RuntimePackSha256);
        }
        args.AddRange(["-cp", classpath, "zombie.network.GameServer", "-cachedir=" + Path.GetFullPath(preset.CachePath), "-servername", preset.Name, "-adminusername", "admin"]);
        if (adminPassword.Length > 0) args.AddRange(["-adminpassword", adminPassword]);
        if (!steam) args.Add("-nosteam");
        var plan = new LaunchPlan { JavaExecutable = Path.Combine(installation, "jre64", "bin", "java.exe"), WorkingDirectory = installation, CacheDirectory = preset.CachePath,
            ConsoleLogPath = Path.Combine(LauncherStorage.Root, "server-logs", preset.Name + ".txt"), Arguments = args };
        if (includeJava && preset.Java.JavaLoaderEnabled)
        {
            var p = preset.JavaProfile(); var catalog = ModCatalog.Scan(p, InstallationLocator.ReadGameVersion(installation));
            var java = JavaMods.Prepare(p, JavaMods.Scan(p, catalog, preset.EnabledMods(), installation, "server"), installation, "server");
            RuntimeClients.CheckOverlayConflicts(classpath.Split(';').Where(p => Path.IsPathRooted(p)), java);
            plan = JavaMods.Attach(plan, java);
        }
        return plan;
    }
}
internal sealed class ServerSession : IDisposable
{
    private readonly Process process;
    private readonly object sync = new();
    private readonly Queue<string> lines = new();
    private readonly string password;
    internal ServerPreset Preset { get; }
    internal bool Running => !process.HasExited;
    internal string Output { get { lock (sync) return string.Join(Environment.NewLine, lines); } }
    internal ServerSession(LaunchPlan plan, ServerPreset preset, string adminPassword)
    {
        Preset = preset; password = adminPassword;
        Directory.CreateDirectory(Path.GetDirectoryName(plan.ConsoleLogPath)!);
        var start = new ProcessStartInfo(plan.JavaExecutable) { WorkingDirectory = plan.WorkingDirectory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true };
        
        start.Environment.Remove("_JAVA_OPTIONS");
        foreach (string arg in plan.Arguments) start.ArgumentList.Add(arg);
        process = new Process { StartInfo = start, EnableRaisingEvents = true };
        void Append(string? value)
        {
            if (value == null) return;
            string clean = password.Length > 0 ? value.Replace(password, "••••") : value;
            lock (sync)
            {
                lines.Enqueue(clean); while (lines.Count > 3000) lines.Dequeue();
                try
                {
                    if (File.Exists(plan.ConsoleLogPath) && new FileInfo(plan.ConsoleLogPath).Length > 16 * 1024 * 1024)
                        File.Move(plan.ConsoleLogPath, plan.ConsoleLogPath + ".previous", true);
                    File.AppendAllText(plan.ConsoleLogPath, clean + Environment.NewLine);
                }
                catch (IOException) { }
            }
        }
        process.OutputDataReceived += (_, e) => Append(e.Data); process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
    }
    internal void Command(string command)
    {
        if (!Running || command.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new IOException(T("server.notRunning"));
        process.StandardInput.WriteLine(command); process.StandardInput.Flush();
    }
    public void Dispose() { process.Dispose(); }
}
