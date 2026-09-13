using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;


internal sealed class ServerRuntimePack
{
    public int Format { get; set; }
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string GameJarSha256 { get; set; } = "";
    public List<RuntimePackMod> Mods { get; set; } = [];
    public List<RuntimePackFile> Files { get; set; } = [];
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    internal const string ManifestName = "pz-runtime-pack.json";
    internal static bool HashEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool HashValid(string? value) => value != null && Regex.IsMatch(value, "^[a-fA-F0-9]{64}$");
    private static bool Identifier(string? value) => value != null && Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$");
    internal static string Manifest(ServerPreset preset) => WorldFiles.Within(preset.CachePath, "runtime-pack/" + ManifestName);
    internal static string Manifest(PlayerProfile profile) => WorldFiles.Within(profile.CachePath, "runtime-pack/" + ManifestName);

    internal static ServerRuntimePack Read(string manifest, string installation)
    {
        if (new FileInfo(manifest).Length > 4_000_000) throw new InvalidDataException(T("pack.invalid"));
        var pack = JsonSerializer.Deserialize<ServerRuntimePack>(File.ReadAllText(manifest), Json) ?? throw new InvalidDataException(T("pack.invalid"));
        if (pack.Format != 1 || !Identifier(pack.Id) || string.IsNullOrWhiteSpace(pack.Version) || pack.Version.Length > 100 ||
            string.IsNullOrWhiteSpace(pack.GameVersion) || !HashValid(pack.GameJarSha256) || pack.Mods is not { Count: > 0 and <= 1000 } || pack.Files is not { Count: > 0 and <= 30000 })
            throw new InvalidDataException(T("pack.invalid"));
        if (!HashEquals(WorldFiles.Hash(Path.Combine(installation, "projectzomboid.jar")), pack.GameJarSha256))
            throw new InvalidDataException(T("pack.gameMismatch", pack.GameVersion));
        if (pack.Mods.Any(m => m == null || !Identifier(m.Id) || !Identifier(m.Folder)) ||
            pack.Mods.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != pack.Mods.Count ||
            pack.Mods.Select(m => m.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pack.Mods.Count)
            throw new InvalidDataException(T("pack.invalid"));
        string root = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in pack.Files)
        {
            if (file == null || string.IsNullOrEmpty(file.Path) || file.Path.Length > 500 || file.Path.Contains('\\') || file.Path.Split('/').Any(p => p is "" or "." or ".." || p.IndexOfAny([':', ';', '\r', '\n']) >= 0) ||
                !HashValid(file.Sha256) || file.Side is not ("client" or "server" or "both") || !seen.Add(file.Path))
                throw new InvalidDataException(T("pack.invalid"));
            bool mod = file.Kind == "mod" && file.Side == "both" && pack.Mods.Any(m => file.Path.StartsWith("mods/" + m.Folder + "/", StringComparison.Ordinal));
            bool java = file.Kind == "java" && file.Path.StartsWith("java/", StringComparison.Ordinal) && file.Path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            if (!mod && !java) throw new InvalidDataException(T("pack.invalid"));
            VerifyFile(root, file);
        }
        
        var actual = WorldFiles.Enumerate(root).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Where(p => p != ManifestName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(seen)) throw new InvalidDataException(T("pack.extraFiles"));
        foreach (var mod in pack.Mods)
        {
            var info = pack.Files.Where(f => f.Path.StartsWith("mods/" + mod.Folder + "/", StringComparison.Ordinal) && f.Path.EndsWith("/mod.info", StringComparison.Ordinal));
            if (!info.Any(f => File.ReadLines(WorldFiles.Within(root, f.Path)).Any(l => l.Trim() == "id=" + mod.Id)))
                throw new InvalidDataException(T("pack.modMissing", mod.Id));
        }
        pack.JavaClasspath(root, "server");
        pack.JavaClasspath(root, "client");
        return pack;
    }
    private static void VerifyFile(string root, RuntimePackFile file)
    {
        string path = WorldFiles.Within(root, file.Path);
        if (!File.Exists(path) || !HashEquals(WorldFiles.Hash(path), file.Sha256)) throw new InvalidDataException(T("pack.fileChanged", file.Path));
    }
    internal List<string> JavaClasspath(string root, string side)
    {
        if (side is not ("server" or "client")) throw new ArgumentException(nameof(side));
        var paths = new List<string>(); var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Files.Where(f => f.Kind == "java" && (f.Side == side || f.Side == "both")))
        {
            string path = WorldFiles.Within(root, file.Path);
            if (path.Contains(Path.PathSeparator)) throw new InvalidDataException(T("pack.classpath", path));
            using var jar = ZipFile.OpenRead(path);
            foreach (var entry in jar.Entries.Where(e => e.FullName.EndsWith(".class", StringComparison.Ordinal)))
            {
                if (entry.FullName.StartsWith("META-INF/versions/", StringComparison.Ordinal)) throw new InvalidDataException(T("pack.invalid"));
                if (!classes.TryAdd(entry.FullName, file.Path)) throw new InvalidDataException(T("pack.overlap", entry.FullName, classes[entry.FullName], file.Path));
            }
            
            var metadata = jar.GetEntry("META-INF/MANIFEST.MF");
            if (metadata != null)
            {
                if (metadata.Length > 65536) throw new InvalidDataException(T("pack.invalid"));
                using var reader = new StreamReader(metadata.Open());
                if (Regex.IsMatch(reader.ReadToEnd(), "^Class-Path:", RegexOptions.Multiline | RegexOptions.IgnoreCase)) throw new InvalidDataException(T("pack.invalid"));
            }
            paths.Add(path);
        }
        return paths;
    }
    internal static ServerPreset Create(string manifest, string installation, string name, string parent, bool steam)
    {
        var pack = Read(manifest, installation);
        string hash = WorldFiles.Hash(manifest);
        var preset = new ServerPreset { Name = name, Steam = steam, RuntimePackSha256 = hash };
        preset.Validate();
        preset.CachePath = WorldFiles.Within(parent, name + "-" + Guid.NewGuid().ToString("N"));
        string root = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        string destination = Path.GetDirectoryName(Manifest(preset))!;
        foreach (var file in pack.Files)
        {
            Copy(WorldFiles.Within(root, file.Path), WorldFiles.Within(destination, file.Path));
            if (file.Kind == "mod") Copy(WorldFiles.Within(root, file.Path), WorldFiles.Within(preset.CachePath, file.Path));
        }
        Copy(manifest, Manifest(preset));
        
        VerifyInstalled(preset, installation, false);
        ServerFiles.Create(preset);
        var original = ServerFiles.Read(preset); var edited = new Dictionary<string, string>(original);
        edited[".ini"] = ServerFiles.UpdateValue(edited[".ini"], "Mods", string.Join(';', pack.Mods.Select(m => "\\" + m.Id)));
        ServerFiles.Save(preset, original, edited);
        return preset;
    }
    internal static void Copy(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, false);
    }
    internal static ServerRuntimePack VerifyInstalled(ServerPreset preset, string installation, bool checkConfiguration = true)
    {
        var pack = VerifyFiles(preset.CachePath, preset.RuntimePackSha256, installation);
        if (checkConfiguration)
        {
            var enabled = preset.EnabledMods();
            var catalog = ModCatalog.Scan(preset.JavaProfile(), InstallationLocator.ReadGameVersion(installation));
            VerifyMods(pack, preset.CachePath, catalog, enabled);
        }
        return pack;
    }
    internal static ServerRuntimePack VerifyFiles(string cache, string hash, string installation)
    {
        string manifest = WorldFiles.Within(cache, "runtime-pack/" + ManifestName);
        if (!HashValid(hash) || !HashEquals(WorldFiles.Hash(manifest), hash)) throw new InvalidDataException(T("pack.manifestChanged"));
        var pack = Read(manifest, installation);
        foreach (var file in pack.Files.Where(f => f.Kind == "mod")) VerifyFile(cache, file);
        foreach (var mod in pack.Mods)
        {
            var expected = pack.Files.Where(f => f.Kind == "mod" && f.Path.StartsWith("mods/" + mod.Folder + "/", StringComparison.Ordinal)).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actual = WorldFiles.Enumerate(WorldFiles.Within(cache, "mods/" + mod.Folder)).Select(p => Path.GetRelativePath(cache, p).Replace('\\', '/'));
            if (!expected.SetEquals(actual)) throw new InvalidDataException(T("pack.extraFiles"));
        }
        return pack;
    }
    internal static void VerifyMods(ServerRuntimePack pack, string cache, List<InstalledMod> catalog, IEnumerable<string> enabled)
    {
        foreach (var mod in pack.Mods)
        {
            var entry = catalog.SingleOrDefault(m => m.Id == mod.Id);
            if (!enabled.Contains(mod.Id) || entry is not { Available: true }) throw new InvalidDataException(T("pack.modMissing", mod.Id));
            string expected = WorldFiles.Within(cache, "mods/" + mod.Folder);
            if (!entry.Location.Equals(expected, StringComparison.OrdinalIgnoreCase) || entry.Copies.Any(c => !c.Location.Equals(expected, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(T("pack.modDuplicate", mod.Id));
        }
    }
    internal static IEnumerable<string> BackupFiles(ServerPreset preset)
    {
        if (preset.RuntimePackSha256.Length == 0) return [];
        
        var pack = JsonSerializer.Deserialize<ServerRuntimePack>(File.ReadAllText(Manifest(preset)), Json) ?? throw new InvalidDataException(T("pack.invalid"));
        return WorldFiles.Enumerate(Path.GetDirectoryName(Manifest(preset))!).Concat(pack.Mods.SelectMany(m => WorldFiles.Enumerate(WorldFiles.Within(preset.CachePath, "mods/" + m.Folder))));
    }
}
internal sealed class RuntimePackMod
{
    public string Id { get; set; } = "";
    public string Folder { get; set; } = "";
}
internal sealed class RuntimePackFile
{
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Side { get; set; } = "both";
}
