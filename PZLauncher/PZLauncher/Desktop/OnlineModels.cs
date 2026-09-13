using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal sealed class OnlineServerInfo
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 16261;
    public int QueryPort { get; set; } = 16261;
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public int Ping { get; set; }
    public string Version { get; set; } = "";
    public string Map { get; set; } = "";
    public string Tags { get; set; } = "";
    public bool PasswordProtected { get; set; }
    public bool Responded { get; set; }
    public Dictionary<string, string> Rules { get; set; } = [];
    public DateTimeOffset CheckedAt { get; set; }
    public ServerModCapture? FullMods { get; set; }
    [JsonIgnore] public List<string> ModIds => FullMods != null ? OnlineProfiles.ParseModIds(string.Join(";", FullMods.Mods.Select(m => m.Id))) : OnlineProfiles.ParseModIds(Rules.GetValueOrDefault("mods", ""));
    [JsonIgnore] public int? ModCount => FullMods?.Mods.Count ?? (int.TryParse(Rules.GetValueOrDefault("modCount"), out int n) && n >= 0 ? n : null);
    [JsonIgnore] public bool CompleteModList => FullMods != null || ModCount.HasValue && ModCount.Value == ModIds.Count;
    [JsonIgnore] public bool? HasMods => FullMods != null ? FullMods.Mods.Count > 0 : Tags.Split(';').Contains("modded") ? true : Tags.Split(';').Contains("vanilla") ? false : ModCount.HasValue ? ModCount > 0 : null;
}
internal sealed class OnlineFavorite
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 16261;
    public int QueryPort { get; set; } = 16261;
    public bool Steam { get; set; } = true;
    public string ProfileId { get; set; } = "";
    public List<string> WorkshopIds { get; set; } = [];
    public List<string> ModIds { get; set; } = [];
    public bool ModListConfirmed { get; set; }
    public OnlineServerInfo? LastInfo { get; set; }
    [JsonIgnore] public string Password { get; set; } = "";
    [JsonIgnore] public string Endpoint => Host + ":" + Port;
    public override string ToString() => Name;
}
internal static class OnlineProfiles
{
    internal static bool SameVersion(Version a, Version b) => a.Major == b.Major && a.Minor == b.Minor && Math.Max(0, a.Build) == Math.Max(0, b.Build);
    internal static string Host(string text)
    {
        text = text.Trim();
        
        if (text.Length is 0 or > 253 || text.Contains(':') || text.Any(char.IsWhiteSpace) ||
            Uri.CheckHostName(text) is not (UriHostNameType.Dns or UriHostNameType.IPv4) ||
            text.StartsWith('-') || text.StartsWith('+'))
            throw new InvalidDataException(T("online.hostInvalid"));
        return text.ToLowerInvariant();
    }
    internal static void Validate(OnlineFavorite favorite)
    {
        favorite.Host = Host(favorite.Host);
        if (favorite.Port is < 1 or > 65535 || favorite.QueryPort is < 1 or > 65535)
            throw new InvalidDataException(T("online.portInvalid"));
        favorite.Name = favorite.Name.Trim();
        if (favorite.Name.Length is 0 or > 100 || favorite.Name.Any(char.IsControl))
            throw new InvalidDataException(T("online.nameInvalid"));
        if (favorite.WorkshopIds.Count > 2000 || favorite.WorkshopIds.Any(id => !WorkshopStore.ValidId(id)))
            throw new InvalidDataException(T("workshop.idCount"));
        favorite.WorkshopIds = favorite.WorkshopIds.Distinct().ToList();
        favorite.ModIds = ParseModIds(string.Join(";", favorite.ModIds));
    }
    internal static List<string> ParseModIds(string text)
    {
        var ids = text.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.TrimStart('\\')).Where(s => s.Length > 0 && s != "+").Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count > 2000 || ids.Any(s => s.Length > 256 || s.Any(char.IsControl)))
            throw new InvalidDataException(T("online.modsInvalid"));
        return ids;
    }
    internal static PlayerProfile Save(LauncherState state, OnlineFavorite favorite, PlayerProfile basis, string? root = null)
    {
        Validate(favorite);
        var target = state.Profiles.SingleOrDefault(p => p.Id == favorite.ProfileId && p.OnlineServerId == favorite.Id);
        if (target == null)
        {
            target = JsonSerializer.Deserialize<PlayerProfile>(JsonSerializer.Serialize<ProfilePreferences>(basis))!;
            target.CopyLocalJvmFrom(basis);
            target.OnlineServerId = favorite.Id;
            target.Name = favorite.Name;
            target.CachePath = Path.Combine(root ?? LauncherStorage.Root, "online", target.Id, "cache");
            target.JavaLoaderEnabled = false; target.JavaModIds = []; target.JavaModSelections = []; target.JavaModOrder = [];
            target.Launch.ConnectPassword = "";
            Directory.CreateDirectory(target.CachePath);
            string options = Path.Combine(basis.CachePath, "options.ini");
            if (File.Exists(options)) File.Copy(options, Path.Combine(target.CachePath, "options.ini"), false);
            state.Profiles.Add(target); favorite.ProfileId = target.Id;
        }
        if (state.OnlineFavorites.Any(f => f.Id != favorite.Id && f.ProfileId == target.Id))
            throw new InvalidDataException(T("online.profileConflict"));
        target.Name = favorite.Name; target.Steam = favorite.Steam;
        ApplyConnection(target, favorite);
        int index = state.OnlineFavorites.FindIndex(f => f.Id == favorite.Id);
        if (index < 0) state.OnlineFavorites.Add(favorite); else state.OnlineFavorites[index] = favorite;
        return target;
    }
    internal static void ApplyConnection(PlayerProfile target, OnlineFavorite favorite)
    {
        target.Steam = favorite.Steam;
        target.Launch.ModFolders = "mods";
        target.Launch.ConnectAddress = favorite.Endpoint;
        target.Launch.ConnectPassword = favorite.Password;
    }
    internal static (List<string> Mods, List<string> Workshop) ParseServerIni(string contents)
    {
        string mods = "", workshop = "";
        bool foundMods = false, foundWorkshop = false;
        foreach (string line in contents.Split('\n'))
        {
            int at = line.IndexOf('='); if (at < 1) continue;
            string key = line[..at].Trim(), value = line[(at + 1)..].Trim();
            if (key == "Mods") { mods = value; foundMods = true; }
            if (key == "WorkshopItems") { workshop = value; foundWorkshop = true; }
        }
        if (!foundMods || !foundWorkshop) throw new InvalidDataException(T("online.iniInvalid"));
        return (ParseModIds(mods), workshop.Length == 0 ? [] : WorkshopStore.ParseIds(workshop));
    }
}
