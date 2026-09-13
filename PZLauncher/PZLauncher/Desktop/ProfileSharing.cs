using System.Text.Json;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal sealed class PortableConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public ProfilePreferences Preferences { get; set; } = new();
    public Dictionary<string, string> GameOptions { get; set; } = [];
    public List<string> Mods { get; set; } = [];
}
internal static class ProfileSharing
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 24 };
    private static bool PrivateOption(string key) => Regex.IsMatch(key, "password|token|secret|username|account|serverAddress|lastSave|lastGame|lastServer", RegexOptions.IgnoreCase);
    internal static PortableConfiguration Export(PlayerProfile profile, string version, IEnumerable<string> mods)
    {
        var prefs = JsonSerializer.Deserialize<ProfilePreferences>(JsonSerializer.Serialize(profile))!;
        prefs.Launch.DebugConfig = ""; prefs.Launch.ConnectAddress = "";
        return new()
        {
            Name = profile.Name, GameVersion = version, Preferences = prefs,
            Mods = mods.Distinct().ToList(),
            GameOptions = new OptionDocument(Path.Combine(profile.CachePath, "options.ini")).Values
                .Where(p => !PrivateOption(p.Key)).ToDictionary()
        };
    }
    internal static PortableConfiguration Read(string path)
    {
        if (new FileInfo(path).Length > 8_000_000) throw new InvalidDataException(T("sharing.invalid"));
        var config = JsonSerializer.Deserialize<PortableConfiguration>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException(T("sharing.invalid"));
        Validate(config); return config;
    }
    internal static void Validate(PortableConfiguration config)
    {
        if (config.SchemaVersion != 1 || string.IsNullOrWhiteSpace(config.Name) || config.Name.Length > 64 ||
            config.Preferences == null || config.Preferences.Launch == null || config.Mods == null || config.GameOptions == null ||
            config.Mods.Count > 5000 || config.GameOptions.Count > 5000) throw new InvalidDataException(T("sharing.invalid"));
        ValidatePreferences(config.Preferences);
        if (config.Preferences.Launch.DebugConfig.Length > 0 || config.Preferences.Launch.ConnectAddress.Length > 0)
            throw new InvalidDataException(T("sharing.private"));
        foreach (var option in config.GameOptions)
            if (option.Key.Length == 0 || option.Key.Length > 200 || option.Key.IndexOfAny(['=', '\r', '\n', '\0']) >= 0 ||
                option.Value == null || option.Value.Length > 8192 || option.Value.IndexOfAny(['\r', '\n', '\0']) >= 0 || PrivateOption(option.Key))
                throw new InvalidDataException(T("sharing.invalid"));
        if (config.Mods.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 200 || id.IndexOfAny([',', '{', '}', '\\', '\r', '\n', '\0']) >= 0))
            throw new InvalidDataException(T("sharing.invalid"));
    }
    internal static void ValidatePreferences(ProfilePreferences p)
    {
        if (!JavaMods.Loaders.Contains(p.JavaLoader) || p.JavaModSelections == null || p.JavaModOrder == null ||
            p.JavaModSelections.Concat(p.JavaModOrder).Any(pair => !JavaMods.Loaders.Contains(pair.Key) || pair.Value == null || pair.Value.Count > 1000 ||
                pair.Value.Any(id => id == null || !Regex.IsMatch(id, "^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,99}$"))))
            throw new InvalidDataException(T("sharing.invalid"));
        if (p.JavaModIds == null || p.JavaModIds.Count > 1000 || p.JavaModIds.Any(id => id == null || !Regex.IsMatch(id, "^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,99}$")))
            throw new InvalidDataException(T("sharing.invalid"));
        if (p.MemoryMb is < 0 or > 65536 || p.InitialMemoryMb is < 0 or > 65536 ||
            p.MemoryMb is > 0 and < 1024 || p.InitialMemoryMb > 0 && p.MemoryMb > 0 && p.InitialMemoryMb > p.MemoryMb ||
            p.StackKb != 0 && p.StackKb is < 256 or > 16384 || p.PauseTargetMs is < 0 or > 2000 ||
            p.ConsoleLogSizeKb is < 1024 or > 2097152 || p.Collector is not ("Jeu" or "G1" or "ZGC"))
            throw new InvalidDataException(T("jvm.invalid"));
        p.Launch.Validate();
    }
    internal static PlayerProfile Import(PortableConfiguration config, string profilesDirectory)
    {
        Validate(config); 
        var profile = JsonSerializer.Deserialize<PlayerProfile>(JsonSerializer.Serialize(config.Preferences))!;
        profile.Name = config.Name;
        profile.CachePath = Path.Combine(profilesDirectory, profile.Id, "cache");
        Directory.CreateDirectory(profile.CachePath);
        new OptionDocument(Path.Combine(profile.CachePath, "options.ini")).SaveChanges(config.GameOptions);
        var selection = new ModSelection(profile.CachePath);
        selection.Ids.AddRange(config.Mods.Distinct()); selection.Save();
        return profile;
    }
}
