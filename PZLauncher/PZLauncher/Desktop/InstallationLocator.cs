using Microsoft.Win32;
using System.Security;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal sealed record GameInstallation(string Path, string Source, Version? Version)
{
    public override string ToString() => $"{(Source == "manual" ? T("install.manual") : Source == "gog" ? "GOG.com" : "Steam")}  ·  {Version?.ToString() ?? "?"}  ·  {Path}";
}

internal static class InstallationLocator
{
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
    private static HashSet<string> registeredGog = new(Paths);
    public static Version? ReadGameVersion(string path) => GameVersionReader.Read(File.Exists(Path.Combine(path, "projectzomboid.jar"))
        ? Path.Combine(path, "projectzomboid.jar") : Path.Combine(LooseCodeRoot(path), "zombie", "core", "Core.class"));
    private static string LooseCodeRoot(string path) => File.Exists(Path.Combine(path, "java", "zombie", "gameStates", "MainScreenState.class")) ? Path.Combine(path, "java") : path;
    internal static bool HasGameCode(string path) => File.Exists(Path.Combine(path, "projectzomboid.jar")) ||
        File.Exists(Path.Combine(LooseCodeRoot(path), "zombie", "gameStates", "MainScreenState.class"));
    public static bool IsValid(string path) => !string.IsNullOrWhiteSpace(path) &&
        HasGameCode(path) &&
        File.Exists(Path.Combine(path, "jre64", "bin", "java.exe"));

    internal static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        if (File.Exists(full) && new[] { "ProjectZomboid64.exe", "ProjectZomboid64.json", "projectzomboid.jar" }.Contains(Path.GetFileName(full), Paths))
            full = Path.GetDirectoryName(full)!;
        return Path.TrimEndingDirectorySeparator(full);
    }
    private static string Clean(string? path)
    {
        try { return Normalize(path); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return ""; }
    }
    internal static bool IsGog(string path)
    {
        string normalized = Clean(path);
        if (registeredGog.Contains(normalized)) return true;
        try { return Directory.Exists(normalized) && Directory.EnumerateFiles(normalized, "goggame-*.info").Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static IReadOnlyList<string> SteamLibraries(IEnumerable<string?>? roots = null)
    {
        var candidates = (roots ?? SteamRoots()).Select(Clean).Where(p => p.Length > 0).Distinct(Paths).ToList();
        var libraries = new HashSet<string>(candidates, Paths);
        foreach (string candidate in candidates)
            foreach (string file in new[] { Path.Combine(candidate, "steamapps", "libraryfolders.vdf"), Path.Combine(candidate, "config", "libraryfolders.vdf") })
            {
                var data = ReadVdf(file);
                if (data.GetValueOrDefault("libraryfolders") is not Dictionary<string, object> folders) continue;
                foreach (var entry in folders.Where(e => uint.TryParse(e.Key, out _)))
                {
                    string value = entry.Value is string legacy ? legacy :
                        entry.Value is Dictionary<string, object> modern ? modern.GetValueOrDefault("path") as string ?? "" : "";
                    string path = Clean(value);
                    if (path.Length > 0 && Path.IsPathFullyQualified(value)) libraries.Add(path);
                }
            }
        return libraries.ToArray();
    }
    internal static IReadOnlyList<GameInstallation> Discover(string preferred = "", IEnumerable<string>? remembered = null,
        IEnumerable<string?>? steamRoots = null, IEnumerable<string>? gogPaths = null)
    {
        var found = new Dictionary<string, GameInstallation>(Paths);
        void Add(string? raw, string source)
        {
            string path = Clean(raw);
            if (!IsValid(path) || found.ContainsKey(path)) return;
            if (IsGog(path)) source = "gog";
            found[path] = new(path, source, ReadGameVersion(path));
        }
        foreach (string library in SteamLibraries(steamRoots))
        {
            string common = Path.Combine(library, "steamapps", "common");
            var manifest = ReadVdf(Path.Combine(library, "steamapps", "appmanifest_108600.acf"));
            if (manifest.GetValueOrDefault("AppState") is Dictionary<string, object> app && app.GetValueOrDefault("appid") as string == "108600" &&
                app.GetValueOrDefault("installdir") is string folder && folder.Length > 0 && !Path.IsPathRooted(folder))
            {
                string path = Clean(Path.Combine(common, folder));
                if (path.StartsWith(Path.GetFullPath(common) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Add(path, "steam");
            }
            Add(Path.Combine(common, "ProjectZomboid"), "steam");
        }
        foreach (string path in gogPaths ?? GogPaths()) Add(path, "gog");
        Add(preferred, "manual");
        foreach (string path in remembered ?? []) Add(path, "manual");
        return found.Values.OrderByDescending(i => Paths.Equals(i.Path, Clean(preferred)))
            .ThenBy(i => i.Source == "steam" ? 0 : i.Source == "gog" ? 1 : 2).ThenBy(i => i.Path, Paths).ToArray();
    }
    public static string Find(string preferred)
    {
        
        if (!string.IsNullOrWhiteSpace(preferred)) return Clean(preferred) is { Length: > 0 } normalized ? normalized : preferred;
        return Discover().FirstOrDefault()?.Path ?? "";
    }
    internal static void Remember(LauncherState state, string path)
    {
        string normalized = Normalize(path);
        if (!IsValid(normalized)) throw new InvalidDataException(T("install.invalid"));
        string previous = state.Installation;
        state.Installation = normalized;
        state.InstallationPaths = new[] { normalized, previous }.Concat(state.InstallationPaths).Select(Clean)
            .Where(p => p.Length > 0).Distinct(Paths).Take(20).ToList();
    }
    private static IEnumerable<string?> SteamRoots()
    {
        var result = new List<string?> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam") };
        VisitRegistry(@"SOFTWARE\Valve\Steam", key =>
        {
            result.Add(key.GetValue("SteamPath") as string); result.Add(key.GetValue("InstallPath") as string);
        });
        return result;
    }
    private static IEnumerable<string> GogPaths()
    {
        var paths = new HashSet<string>(Paths);
        void Add(string? raw) { string value = Clean(raw); if (value.Length > 0) paths.Add(value); }
        VisitRegistry(@"SOFTWARE\GOG.com\Games", games =>
        {
            foreach (string id in games.GetSubKeyNames())
                ReadSubKey(games, id, game => Add(game.GetValue("path") as string));
        });
        VisitRegistry(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", programs =>
        {
            foreach (string name in programs.GetSubKeyNames())
                ReadSubKey(programs, name, program =>
                {
                    if ((program.GetValue("Publisher") as string)?.Contains("GOG", StringComparison.OrdinalIgnoreCase) == true)
                        Add(program.GetValue("InstallLocation") as string);
                });
        });
        registeredGog = new(paths, Paths);
        void Probe(string path) { if (IsGog(path)) Add(path); }
        Probe(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "GOG Games", "Project Zomboid"));
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string root = Environment.GetFolderPath(special);
            Probe(Path.Combine(root, "GOG Galaxy", "Games", "Project Zomboid"));
            Probe(Path.Combine(root, "GOG.com", "Project Zomboid"));
        }
        return paths;
    }
    private static void VisitRegistry(string path, Action<RegistryKey> read)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    ReadSubKey(root, path, read);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { }
    }
    private static void ReadSubKey(RegistryKey root, string name, Action<RegistryKey> read)
    {
        try { using var key = root.OpenSubKey(name); if (key != null) read(key); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { }
    }
    private static Dictionary<string, object> ReadVdf(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 2_000_000) return new(StringComparer.OrdinalIgnoreCase);
            return ParseVdf(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { LauncherStorage.Log("Installation detection: " + path + ": " + ex.Message); return new(StringComparer.OrdinalIgnoreCase); }
    }
    internal static Dictionary<string, object> ParseVdf(string text)
    {
        
        var tokens = Regex.Matches(text, "\"(?:\\\\.|[^\"\\\\])*\"|//[^\\r\\n]*|[{}]|[^\\s]+")
            .Select(m => m.Value).Where(t => !t.StartsWith("//")).ToArray();
        int at = 0;
        string Value(string token)
        {
            if (token.Length < 2 || token[0] != '"' || token[^1] != '"') throw new InvalidDataException("Invalid Steam KeyValues string.");
            return token[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        Dictionary<string, object> Read(int depth)
        {
            if (depth > 16) throw new InvalidDataException("Steam KeyValues nesting exceeds limit.");
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (at < tokens.Length)
            {
                string token = tokens[at++];
                if (token == "}" && depth > 0) return result;
                string key = Value(token);
                if (at >= tokens.Length) throw new InvalidDataException("Incomplete Steam KeyValues value.");
                string value = tokens[at++];
                result[key] = value == "{" ? Read(depth + 1) : Value(value);
            }
            if (depth > 0) throw new InvalidDataException("Unclosed Steam KeyValues object.");
            return result;
        }
        return Read(0);
    }
}
