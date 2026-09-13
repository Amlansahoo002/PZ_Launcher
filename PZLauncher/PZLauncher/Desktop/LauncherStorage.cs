using System.Text;
using System.Text.Json;
using PZLauncher.Profiles;

namespace PZLauncher.Desktop;

internal class ProfilePreferences
{
    public bool Steam { get; set; } = true;
    public bool Voice { get; set; } = true;
    public bool Debug { get; set; }
    public int MemoryMb { get; set; }
    public int InitialMemoryMb { get; set; }
    public int StackKb { get; set; }
    public int PauseTargetMs { get; set; }
    public bool GcLogging { get; set; }
    public bool StringDeduplication { get; set; }
    public bool NetworkLogging { get; set; }
    public int ConsoleLogSizeKb { get; set; } = 1024000;
    public LaunchSettings Launch { get; set; } = new();
    public bool JavaLoaderEnabled { get; set; }
    public string JavaLoader { get; set; } = JavaMods.Community;
    public List<string> JavaModIds { get; set; } = [];
    public Dictionary<string, List<string>> JavaModSelections { get; set; } = [];
    public Dictionary<string, List<string>> JavaModOrder { get; set; } = [];
    public string Collector { get; set; } = "Jeu";
    public bool AutomaticJvm { get; set; }
}
internal sealed class PlayerProfile : ProfilePreferences
{
    public string JvmArgumentBase { get; set; } = "launcher";
    public List<string> ExtraJvmArguments { get; set; } = [];
    public bool DirectLooseClassesFirst { get; set; }
    public string OnlineServerId { get; set; } = "";
    public string DedicatedServerId { get; set; } = "";
    public string RuntimePackSha256 { get; set; } = "";
    public string RuntimePackId { get; set; } = "";
    public string RuntimePackVersion { get; set; } = "";
    public string LeafLibraryPath { get; set; } = "";
    public string ZombieBuddyAgentPath { get; set; } = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "My game";
    public string CachePath { get; set; } = LauncherStorage.DefaultGameData;
    internal void CopyLocalJvmFrom(PlayerProfile source)
    {
        JvmArgumentBase = source.JvmArgumentBase;
        ExtraJvmArguments = source.ExtraJvmArguments.ToList();
        DirectLooseClassesFirst = source.DirectLooseClassesFirst;
    }
    public GameProfile AsGameProfile()
    {
        if (OnlineServerId.Length > 0) Launch.ModFolders = "mods";
        return new() { Id = Id, Name = Name, CacheDir = CachePath, Kind = OnlineServerId.Length > 0 || DedicatedServerId.Length > 0 ? GameProfileKind.Multiplayer : GameProfileKind.Solo, Launch = Launch };
    }
    public JvmProfile AsJvmProfile()
    {
        JvmArguments.Validate(JvmArgumentBase, ExtraJvmArguments);
        return new()
        {
        Id = Id, Name = Name, SteamEnabled = Steam, NoVoip = !Voice,
        DebugEnabled = Debug || Launch.ImGui || Launch.ImGuiViewports, XmxMb = MemoryMb, XmsMb = InitialMemoryMb,
        StackKb = StackKb, PauseTargetMs = PauseTargetMs, GcLogging = GcLogging, StringDeduplication = StringDeduplication,
        ZNetLogEnabled = NetworkLogging, ConsoleLogSizeKb = ConsoleLogSizeKb, UseZgc = Collector == "ZGC",
        UseOptimizedG1 = Collector == "G1", ArgumentBase = JvmArgumentBase, ExtraJvmArguments = ExtraJvmArguments.ToArray()
        };
    }
    public override string ToString() => Name;
}

internal sealed class LauncherState
{
    public string Language { get; set; } = "en";
    public string Installation { get; set; } = "";
    public List<string> InstallationPaths { get; set; } = [];
    public string SelectedProfile { get; set; } = "";
    public List<PlayerProfile> Profiles { get; set; } = [new()];
    public List<ServerPreset> Servers { get; set; } = [];
    public List<RuntimeRevision> RuntimeRevisions { get; set; } = [];
    public List<OnlineFavorite> OnlineFavorites { get; set; } = [];
    public MapPreviewSettings MapPreview { get; set; } = new();
    public WindowPlacement? Window { get; set; }
}

internal static class LauncherStorage
{
    private static readonly string? DataOverride = Environment.GetEnvironmentVariable("PZLAUNCHER_DATA");
    public static readonly string Root = Path.GetFullPath(DataOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PZLauncher"));
    public static readonly string UserGameData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid");
    
    public static readonly string DefaultGameData = DataOverride == null ? UserGameData : Path.Combine(Root, "game-data");
    public static string LogPath => Path.Combine(Root, "launcher.log");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static LauncherState Load()
    {
        Directory.CreateDirectory(Root);
        string path = Path.Combine(Root, "launcher.json");
        if (!File.Exists(path)) return new();
        try
        {
            var state = JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(path)) ?? new();
            state.Profiles = state.Profiles.Where(p => !string.IsNullOrWhiteSpace(p.CachePath)).ToList();
            if (state.Profiles.Count == 0) state.Profiles.Add(new());
            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new InvalidDataException(T("error.profiles") + path, ex);
        }
    }

    public static void Save(LauncherState state) =>
        WriteAtomic(Path.Combine(Root, "launcher.json"), JsonSerializer.Serialize(state, JsonOptions));

    public static void WriteAtomic(string path, string contents)
        => WriteAtomicBytes(path, new UTF8Encoding(false).GetBytes(contents));

    internal static void WriteAtomicBytes(string path, byte[] contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, contents);
            if (File.Exists(path)) File.Replace(temp, path, path + ".pzlauncher.bak", true);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Root);
            lock (JsonOptions)
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 4_000_000)
                    File.Move(LogPath, LogPath + ".previous", true);
                File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {text}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
