using System.Text.Json;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal sealed class WorkshopModFolder
{
    public string Folder { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> ModIds { get; set; } = [];
}
internal sealed class WorkshopInstall
{
    public string ItemId { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public string Manifest { get; set; } = "";
    public long WorkshopUpdated { get; set; }
    public List<WorkshopModFolder> Mods { get; set; } = [];
}
internal static class WorkshopStore
{
    internal static string IndexPath(string cache) => Path.Combine(cache, ".pzlauncher-workshop", "installed.json");
    internal static bool ValidId(string id) => Regex.IsMatch(id, "^[1-9][0-9]{0,19}$") && ulong.TryParse(id, out _);
    internal static List<string> ParseIds(string text)
    {
        var ids = new List<string>();
        foreach (string token in Regex.Split(text.Trim(), @"[\s,;]+").Where(s => s.Length > 0))
        {
            string id = token;
            if (Uri.TryCreate(token, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme is not ("https" or "http") || !uri.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase) ||
                    uri.AbsolutePath is not ("/sharedfiles/filedetails/" or "/sharedfiles/filedetails" or "/workshop/filedetails/" or "/workshop/filedetails"))
                    throw new InvalidDataException(T("workshop.invalidId", token));
                id = Regex.Match(uri.Query, @"(?:^\?|&)id=([0-9]+)(?:&|$)").Groups[1].Value;
            }
            if (!ValidId(id)) throw new InvalidDataException(T("workshop.invalidId", token));
            if (!ids.Contains(id)) ids.Add(id);
        }
        if (ids.Count is 0 or > 2000) throw new InvalidDataException(T("workshop.idCount"));
        return ids;
    }
    internal static List<WorkshopInstall> Read(string cache)
    {
        string path = IndexPath(cache);
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 8_000_000) throw new InvalidDataException(T("workshop.indexInvalid"));
        var items = JsonSerializer.Deserialize<List<WorkshopInstall>>(File.ReadAllText(path)) ?? throw new InvalidDataException(T("workshop.indexInvalid"));
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemIds = new HashSet<string>();
        foreach (var item in items)
        {
            if (item == null || !ValidId(item.ItemId) || !itemIds.Add(item.ItemId) || item.Mods == null)
                throw new InvalidDataException(T("workshop.indexInvalid"));
            foreach (var mod in item.Mods)
            {
                if (mod == null || string.IsNullOrWhiteSpace(mod.Folder) || mod.Folder is "." or ".." ||
                    mod.Folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !folders.Add(mod.Folder) || mod.ModIds == null)
                    throw new InvalidDataException(T("workshop.indexInvalid"));
                WorldFiles.Within(Path.Combine(cache, "mods"), mod.Folder);
            }
        }
        return items;
    }
    internal static string PreferLocal(string order) => string.Join(",", new[] { "mods" }
        .Concat(order.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => s != "mods")));

    internal static WorkshopInstall Install(string cache, string itemId, string download, CancellationToken cancellation, string? sourceManifest = null)
    {
        if (!ValidId(itemId)) throw new InvalidDataException(T("workshop.invalidId", itemId));
        var revision = WorkshopVersions.ReadInstalled(sourceManifest, itemId);
        string control = WorldFiles.Within(cache, ".pzlauncher-workshop");
        Directory.CreateDirectory(control);
        using var installationLock = new FileStream(Path.Combine(control, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var installed = Read(cache);
        var previous = installed.SingleOrDefault(i => i.ItemId == itemId);
        string index = IndexPath(cache);
        string? originalIndex = File.Exists(index) ? File.ReadAllText(index) : null;
        string source = Directory.Exists(Path.Combine(download, "mods")) ? Path.Combine(download, "mods") : Path.Combine(download, "Contents", "mods");
        if (!Directory.Exists(source)) throw new InvalidDataException(T("workshop.noMods", itemId));
        var incoming = new List<(WorkshopModFolder mod, string path, List<string> files)>();
        foreach (string folder in Directory.EnumerateDirectories(source))
        {
            var files = WorldFiles.Enumerate(folder);
            var infos = files.Where(f => Path.GetFileName(f).Equals("mod.info", StringComparison.OrdinalIgnoreCase)).ToList();
            if (infos.Count == 0) continue;
            var mod = new WorkshopModFolder { Folder = Path.GetFileName(folder), Name = Path.GetFileName(folder) };
            foreach (string info in infos)
                foreach (string line in File.ReadLines(info))
                {
                    int at = line.IndexOf('='); if (at < 1) continue;
                    string key = line[..at].Trim(), value = line[(at + 1)..].Trim();
                    if (key.Equals("id", StringComparison.OrdinalIgnoreCase) && value.Length > 0) mod.ModIds.Add(value.Replace("\\", ""));
                    if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && value.Length > 0) mod.Name = value;
                }
            mod.ModIds = mod.ModIds.Distinct().ToList();
            if (mod.ModIds.Count == 0) throw new InvalidDataException(T("workshop.noMods", itemId));
            incoming.Add((mod, folder, files));
        }
        if (incoming.Count == 0) throw new InvalidDataException(T("workshop.noMods", itemId));
        string modsRoot = WorldFiles.Within(cache, "mods"); Directory.CreateDirectory(modsRoot);
        foreach (var entry in incoming)
        {
            string target = WorldFiles.Within(modsRoot, entry.mod.Folder);
            bool owned = previous?.Mods.Any(m => m.Folder.Equals(entry.mod.Folder, StringComparison.OrdinalIgnoreCase)) == true;
            if (File.Exists(target) || Directory.Exists(target) && !owned ||
                installed.Any(i => i.ItemId != itemId && i.Mods.Any(m => m.Folder.Equals(entry.mod.Folder, StringComparison.OrdinalIgnoreCase))))
                throw new IOException(T("workshop.folderConflict", target));
        }
        string transaction = WorldFiles.Within(control, "staging/" + Guid.NewGuid().ToString("N"));
        string backup = WorldFiles.Within(control, "backups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + itemId + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(transaction);
        var replaced = new List<string>(); var added = new List<string>();
        try
        {
            foreach (var entry in incoming)
            {
                string staged = WorldFiles.Within(transaction, entry.mod.Folder); Directory.CreateDirectory(staged);
                foreach (string file in entry.files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string target = WorldFiles.Within(staged, Path.GetRelativePath(entry.path, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
                }
            }
            cancellation.ThrowIfCancellationRequested();
            if (revision != WorkshopVersions.ReadInstalled(sourceManifest, itemId)) throw new IOException(T("online.sourceChanged"));
            if ((File.Exists(index) ? File.ReadAllText(index) : null) != originalIndex) throw new IOException(T("workshop.indexChanged"));
            Directory.CreateDirectory(backup);
            if (originalIndex != null) File.WriteAllText(Path.Combine(backup, "installed.json"), originalIndex);
            foreach (var old in previous?.Mods ?? [])
            {
                string target = WorldFiles.Within(modsRoot, old.Folder);
                if (!Directory.Exists(target)) continue;
                WorldFiles.Enumerate(target); 
                Directory.Move(target, WorldFiles.Within(backup, old.Folder)); replaced.Add(old.Folder);
            }
            foreach (var entry in incoming)
            {
                Directory.Move(WorldFiles.Within(transaction, entry.mod.Folder), WorldFiles.Within(modsRoot, entry.mod.Folder));
                added.Add(entry.mod.Folder);
            }
            var result = new WorkshopInstall { ItemId = itemId, UpdatedAt = DateTimeOffset.Now, Mods = incoming.Select(e => e.mod).ToList(), Manifest = revision.Manifest, WorkshopUpdated = revision.Updated };
            installed.RemoveAll(i => i.ItemId == itemId); installed.Add(result);
            LauncherStorage.WriteAtomic(index, JsonSerializer.Serialize(installed, new JsonSerializerOptions { WriteIndented = true }));
            return result;
        }
        catch
        {
            
            foreach (string folder in added.AsEnumerable().Reverse())
                Directory.Move(WorldFiles.Within(modsRoot, folder), WorldFiles.Within(transaction, folder));
            foreach (string folder in replaced)
                Directory.Move(WorldFiles.Within(backup, folder), WorldFiles.Within(modsRoot, folder));
            throw;
        }
        finally
        {
            
            if (Directory.Exists(transaction)) { WorldFiles.Enumerate(transaction); Directory.Delete(WorldFiles.Within(control, Path.GetRelativePath(control, transaction)), true); }
        }
    }
}
