using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal sealed record WorkshopRevision(string Manifest, long Updated)
{
    internal static WorkshopRevision Unknown => new("", 0);
}
internal static class WorkshopVersions
{
    internal static WorkshopRevision ReadInstalled(string? file, string id)
    {
        if (file == null || !File.Exists(file)) return WorkshopRevision.Unknown;
        if (new FileInfo(file).Length > 16_000_000) throw new InvalidDataException(T("workshop.indexInvalid"));
        return ParseInstalled(File.ReadAllText(file), id);
    }
    internal static WorkshopRevision ParseInstalled(string text, string id)
    {
        
        var tokens = Regex.Matches(text, "\"(?:\\\\.|[^\"\\\\])*\"|[{}]").Select(m => m.Value).ToArray();
        int at = 0;
        Dictionary<string, object> Read(int depth)
        {
            if (depth > 16) throw new InvalidDataException(T("workshop.indexInvalid"));
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (at < tokens.Length)
            {
                string token = tokens[at++];
                if (token == "}" && depth > 0) return result;
                if (!token.StartsWith('"') || at >= tokens.Length) throw new InvalidDataException(T("workshop.indexInvalid"));
                string key = token[1..^1]; string value = tokens[at++];
                result[key] = value == "{" ? Read(depth + 1) : value.StartsWith('"') ? value[1..^1] : throw new InvalidDataException(T("workshop.indexInvalid"));
            }
            if (depth > 0) throw new InvalidDataException(T("workshop.indexInvalid"));
            return result;
        }
        var top = Read(0);
        if (!top.TryGetValue("AppWorkshop", out object? app) || app is not Dictionary<string, object> root ||
            root.GetValueOrDefault("appid") as string != "108600" ||
            root.GetValueOrDefault("WorkshopItemsInstalled") is not Dictionary<string, object> items ||
            items.GetValueOrDefault(id) is not Dictionary<string, object> entry) return WorkshopRevision.Unknown;
        string manifest = entry.GetValueOrDefault("manifest") as string ?? "";
        long.TryParse(entry.GetValueOrDefault("timeupdated") as string, out long updated);
        return new(ulong.TryParse(manifest, out ulong number) && number > 0 ? manifest : "", Math.Max(0, updated));
    }
}
internal sealed record WorkshopPublished(string Id, string Title, long Updated, bool Available);
internal sealed record OnlineModStatus(string Id, string Title, string Manifest, long Installed, long Published, string Status);
internal static class OnlineMods
{
    internal static async Task<Dictionary<string, WorkshopPublished>> PublishedAsync(IEnumerable<string> ids, CancellationToken cancellation)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var result = new Dictionary<string, WorkshopPublished>();
        foreach (var batch in ids.Distinct().Chunk(100))
        {
            if (batch.Any(id => !WorkshopStore.ValidId(id))) throw new InvalidDataException(T("online.modsInvalid"));
            var fields = new List<KeyValuePair<string, string>> { new("itemcount", batch.Length.ToString()) };
            fields.AddRange(batch.Select((id, i) => new KeyValuePair<string, string>($"publishedfileids[{i}]", id)));
            using var body = new FormUrlEncodedContent(fields);
            using var response = await http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", body, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string text = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            if (text.Length > 12_000_000) throw new IOException(T("online.metadataInvalid"));
            foreach (var item in ParsePublished(text, batch)) result[item.Id] = item;
        }
        return result;
    }
    internal static List<WorkshopPublished> ParsePublished(string text, IReadOnlyCollection<string> wanted)
    {
        using var document = JsonDocument.Parse(text);
        var result = new List<WorkshopPublished>();
        foreach (var item in document.RootElement.GetProperty("response").GetProperty("publishedfiledetails").EnumerateArray())
        {
            string id = item.GetProperty("publishedfileid").GetString() ?? "";
            if (!wanted.Contains(id)) continue;
            bool available = item.TryGetProperty("result", out var code) && code.GetInt32() == 1 &&
                item.TryGetProperty("consumer_app_id", out var app) && app.GetInt32() == 108600;
            string name = available && item.TryGetProperty("title", out var title) ? title.GetString() ?? id : id;
            long updated = available && item.TryGetProperty("time_updated", out var time) ? time.GetInt64() : 0;
            result.Add(new(id, name, updated, available));
        }
        return result;
    }
    internal static List<OnlineModStatus> Compare(OnlineFavorite favorite, string cache, IReadOnlyDictionary<string, WorkshopPublished> published)
    {
        var installed = WorkshopStore.Read(cache).ToDictionary(i => i.ItemId);
        return favorite.WorkshopIds.Select(id =>
        {
            installed.TryGetValue(id, out var local); published.TryGetValue(id, out var remote);
            bool filesPresent = local != null && local.Mods.Count > 0 && local.Mods.All(m => Directory.Exists(Path.Combine(cache, "mods", m.Folder)));
            string status = !filesPresent ? "missing" : remote?.Available != true || local!.WorkshopUpdated == 0 || remote.Updated <= 0 ? "unknown" :
                remote.Updated > local.WorkshopUpdated ? "update" : remote.Updated < local.WorkshopUpdated ? "newer" : "current";
            return new OnlineModStatus(id, remote?.Title ?? local?.Mods.FirstOrDefault()?.Name ?? id, local?.Manifest ?? "", local?.WorkshopUpdated ?? 0, remote?.Updated ?? 0, status);
        }).ToList();
    }
    internal static List<InstalledMod> SteamCatalog(Version? gameVersion)
    {
        var source = new PlayerProfile { CachePath = LauncherStorage.DefaultGameData, Steam = true };
        source.Launch.ModFolders = "steam";
        return ModCatalog.Scan(source, gameVersion);
    }
    internal static List<string> IdentifyWorkshop(IEnumerable<string> modIds, IEnumerable<InstalledMod> catalog) =>
        catalog.Where(m => modIds.Contains(m.Id) && WorkshopStore.ValidId(m.WorkshopId)).Select(m => m.WorkshopId).Distinct().ToList();
    internal static async Task<List<WorkshopItemResult>> CopyInstalledAsync(OnlineFavorite favorite, PlayerProfile profile, CancellationToken cancellation, IReadOnlyList<string>? libraries = null)
    {
        var result = new List<WorkshopItemResult>();
        var existing = WorkshopStore.Read(profile.CachePath);
        var roots = (libraries ?? InstallationLocator.SteamLibraries()).Select(l => Path.Combine(l, "steamapps", "workshop")).ToList();
        foreach (string id in favorite.WorkshopIds)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (existing.Any(item => item.ItemId == id && item.Mods.Count > 0 && item.Mods.All(m => Directory.Exists(Path.Combine(profile.CachePath, "mods", m.Folder)))))
                { result.Add(new(id, true, T("online.copyKept"))); continue; }
                string? root = roots.FirstOrDefault(r => Directory.Exists(Path.Combine(r, "content", "108600", id, "mods")));
                if (root == null) { result.Add(new(id, false, T("online.notInSteam"))); continue; }
                GameProcessGuard.EnsureStopped(profile.CachePath);
                var installed = await Task.Run(() => WorkshopStore.Install(profile.CachePath, id, Path.Combine(root, "content", "108600", id),
                    cancellation, Path.Combine(root, "appworkshop_108600.acf")), cancellation).ConfigureAwait(false);
                result.Add(new(id, true, string.Join(", ", installed.Mods.Select(m => m.Name))));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            { result.Add(new(id, false, ex.Message)); }
        }
        return result;
    }
}
