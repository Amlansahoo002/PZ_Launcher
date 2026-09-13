using System.Globalization;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal sealed class OptionDocument
{
    private readonly string path;
    public Dictionary<string, string> Values { get; }
    public OptionDocument(string path)
    {
        this.path = path;
        Values = ReadValues(File.Exists(path) ? File.ReadAllLines(path) : []);
    }
    private static Dictionary<string, string> ReadValues(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.StartsWith('#') || line.StartsWith(';')) continue;
            int split = line.IndexOf('=');
            if (split > 0) values[line[..split].Trim()] = line[(split + 1)..].Trim();
        }
        return values;
    }
    public void SaveChanges(IReadOnlyDictionary<string, string> edits)
    {
        var lines = (File.Exists(path) ? File.ReadAllLines(path) : []).ToList();
        var current = ReadValues(lines);
        foreach (var (key, value) in edits)
        {
            if (key.IndexOfAny(['=', '\r', '\n']) >= 0 || value.IndexOfAny(['\r', '\n']) >= 0)
                throw new InvalidDataException(T("error.newline"));
            Values.TryGetValue(key, out var before);
            current.TryGetValue(key, out var now);
            if (before != now && now != value)
                throw new IOException(T("error.optionConflict", key));
        }
        foreach (var (key, value) in edits)
        {
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                int split = lines[i].IndexOf('=');
                if (split < 0 || lines[i][..split].Trim() != key) continue;
                lines[i] = lines[i][..(split + 1)] + value;
                found = true;
            }
            if (!found) lines.Add(key + "=" + value);
        }
        LauncherStorage.WriteAtomic(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        foreach (var (key, value) in edits) Values[key] = value;
    }
}

internal sealed class InstalledMod
{
    public List<ModCopy> Copies { get; set; } = [];
    public string Issue { get; init; } = "";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Location { get; init; } = "";
    public string Source { get; init; } = "Local";
    public string WorkshopId { get; init; } = "";
    public string Target { get; init; } = "";
    public bool Available { get; init; }
    public string[] Requires { get; init; } = [];
    public string[] LoadAfter { get; init; } = [];
    public string[] LoadBefore { get; init; } = [];
    public string[] Incompatible { get; init; } = [];
}
internal sealed record ModCopy(string Source, string Location, string Target, bool Available, string Issue);

internal sealed class ModSelection
{
    private readonly string path;
    private string original;
    public List<string> Ids { get; }
    private static readonly Regex ModBlock = new(@"\bmods\s*\{([^{}]*)\}", RegexOptions.IgnoreCase);
    public ModSelection(string cachePath)
    {
        path = Path.Combine(cachePath, "mods", "default.txt");
        original = File.Exists(path) ? File.ReadAllText(path) : "VERSION = 1,\n\nmods\n{\n}\n\nmaps\n{\n}\n";
        var block = ModBlock.Match(original);
        Ids = Regex.Matches(block.Groups[1].Value, @"(?m)^\s*mod\s*=\s*([^,\r\n]+)")
            .Select(m => m.Groups[1].Value.Trim().Replace("\\", "")).Distinct(StringComparer.Ordinal).ToList();
    }
    public void Save()
    {
        string current = File.Exists(path) ? File.ReadAllText(path) : original;
        if (current != original) throw new IOException(T("error.modConflict"));
        if (Ids.Any(id => string.IsNullOrWhiteSpace(id) || id.IndexOfAny([',', '{', '}', '\r', '\n']) >= 0))
            throw new InvalidDataException(T("error.modId"));
        var match = ModBlock.Match(original);
        if (!match.Success) throw new InvalidDataException(T("error.modFormat"));
        string block = "mods\n{\n" + string.Join("", Ids.Select(id => "    mod = " + id + ",\n")) + "}";
        string updated = original[..match.Index] + block + original[(match.Index + match.Length)..];
        LauncherStorage.WriteAtomic(path, updated);
        original = updated;
    }
}

internal static class ModCatalog
{
    public static List<InstalledMod> Scan(PlayerProfile profile, Version? gameVersion = null, List<string>? issues = null)
    {
        var mods = new List<InstalledMod>();
        var managed = WorkshopStore.Read(profile.CachePath).SelectMany(item => item.Mods.Select(mod => (item.ItemId, mod.Folder)))
            .ToDictionary(m => m.Folder, m => m.ItemId, StringComparer.OrdinalIgnoreCase);
        var roots = new List<(string path, string source, string workshop)>();
        foreach (string library in InstallationLocator.SteamLibraries())
        {
            string content = Path.Combine(library, "steamapps", "workshop", "content", "108600");
            if (Directory.Exists(content))
                foreach (string item in Directory.EnumerateDirectories(content))
                    roots.Add((Path.Combine(item, "mods"), "Workshop", Path.GetFileName(item)));
        }
        roots.Add((Path.Combine(profile.CachePath, "mods"), "Local", ""));
        string staging = Path.Combine(profile.CachePath, "Workshop");
        if (Directory.Exists(staging))
            foreach (string item in Directory.EnumerateDirectories(staging))
                roots.Add((Path.Combine(item, "Contents", "mods"), "Staged", ""));
        var folderOrder = (profile.OnlineServerId.Length > 0 ? "mods" : profile.Launch.ModFolders).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string RootKind(string source) => source == "Workshop" ? "steam" : source == "Staged" ? "workshop" : "mods";
        foreach (var root in roots.Where(r => !folderOrder.Contains(RootKind(r.source))))
            if (Directory.Exists(root.path)) issues?.Add(T("mods.rootExcluded", root.path));
        foreach (var root in roots.Where(r => folderOrder.Contains(RootKind(r.source))).OrderBy(r => Array.IndexOf(folderOrder, RootKind(r.source))))
        {
            if (!Directory.Exists(root.path)) continue;
            foreach (string modPath in Directory.EnumerateDirectories(root.path))
            {
                if (Path.GetFileName(modPath).Equals("examplemod", StringComparison.OrdinalIgnoreCase) &&
                    !(root.source == "Local" && managed.ContainsKey(Path.GetFileName(modPath)))) continue;
                try
                {
                    string? selected = null;
                    Version maximum = Normalize(gameVersion ?? new Version(42, 0));
                    var versions = Directory.EnumerateDirectories(modPath)
                        .Select(p => (path: p, version: ParseVersion(Path.GetFileName(p))))
                        .Where(v => v.version != null && v.version.Major == 42 && v.version <= maximum)
                        .OrderByDescending(v => v.version).ToList();
                    if (versions.Count > 0) selected = versions[0].path;
                    string commonInfo = Path.Combine(modPath, "common", "mod.info");
                    string versionInfo = selected == null ? "" : Path.Combine(selected, "mod.info");
                    bool b42 = File.Exists(commonInfo) || File.Exists(versionInfo);
                    var files = new List<string>();
                    if (File.Exists(commonInfo)) files.Add(commonInfo);
                    if (File.Exists(versionInfo)) files.Add(versionInfo);
                    if (files.Count == 0 && File.Exists(Path.Combine(modPath, "mod.info")))
                        files.Add(Path.Combine(modPath, "mod.info"));
                    if (files.Count == 0) { issues?.Add(T("mods.noMetadata", modPath)); continue; }
                    var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string info in files)
                        foreach (string line in File.ReadLines(info))
                        {
                            int at = line.IndexOf('=');
                            if (at > 0) fields[line[..at].Trim()] = line[(at + 1)..].Trim();
                        }
                    string id = fields.GetValueOrDefault("id", "").Replace("\\", "");
                    if (id.Length == 0) { issues?.Add(T("mods.noId", modPath)); continue; }
                    string issue = "";
                    bool format = maximum.Major >= 42 ? b42 : !b42;
                    if (!format) issue = T("mods.formatMismatch");
                    foreach (string key in new[] { "versionMin", "versionMax" })
                        if (fields.TryGetValue(key, out string? bound) && ParseVersion(bound) is { } parsed &&
                            (key == "versionMin" ? maximum < parsed : maximum > parsed)) issue = T("mods.versionMismatch", key, bound);
                    if (!profile.Steam && root.source != "Local") issue = T("mods.needsSteam");
                    string managedItem = root.source == "Local" ? managed.GetValueOrDefault(Path.GetFileName(modPath), "") : "";
                    mods.Add(new()
                    {
                        Id = id, Name = fields.GetValueOrDefault("name", id), Location = modPath,
                        Source = managedItem.Length > 0 ? "SteamCMD" : root.source, WorkshopId = managedItem.Length > 0 ? managedItem : root.workshop,
                        Available = issue.Length == 0, Issue = issue,
                        Target = b42 ? selected == null ? "Commun B42" : "B" + Path.GetFileName(selected) : "Ancien format",
                        Requires = MetadataList(fields, "require"), LoadAfter = MetadataList(fields, "loadModAfter"),
                        LoadBefore = MetadataList(fields, "loadModBefore"), Incompatible = MetadataList(fields, "incompatible")
                    });
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { issues?.Add(modPath + ": " + e.Message); LauncherStorage.Log(e.Message); }
            }
        }
        return mods.GroupBy(m => m.Id, StringComparer.Ordinal).Select(g =>
            {
                var chosen = g.FirstOrDefault(m => m.Available) ?? g.First();
                chosen.Copies = g.Select(m => new ModCopy(m.Source, m.Location, m.Target, m.Available, m.Issue)).ToList();
                return chosen;
            })
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    private static Version? ParseVersion(string value) =>
        Version.TryParse(value.Contains('.') ? value : value + ".0", out var v) ? Normalize(v) : null;
    private static Version Normalize(Version value) => new(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
    internal static string[] MetadataList(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.GetValueOrDefault(key, "").Replace("\\", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
}

internal sealed record SaveEntry(string Name, string Mode, string Path, DateTime Modified, int Files)
{
    public override string ToString() => Name;
}
internal static class SaveCatalog
{
    public static List<SaveEntry> Scan(string cachePath)
    {
        string root = Path.Combine(cachePath, "Saves");
        if (!Directory.Exists(root)) return [];
        var saves = new List<SaveEntry>();
        foreach (string mode in Directory.EnumerateDirectories(root))
            foreach (string path in Directory.EnumerateDirectories(mode))
            {
                try
                {
                    var files = new DirectoryInfo(path).GetFiles();
                    saves.Add(new(Path.GetFileName(path), Path.GetFileName(mode), path,
                        files.Length > 0 ? files.Max(f => f.LastWriteTime) : Directory.GetLastWriteTime(path), files.Length));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LauncherStorage.Log(e.Message); }
            }
        return saves.OrderByDescending(s => s.Modified).ToList();
    }
}
