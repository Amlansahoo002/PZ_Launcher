using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal static partial class JavaMods
{
    internal const string Community = "pzlauncher", Leaf = "leaf", ZombieBuddy = "zombiebuddy", None = "none";
    internal static readonly string[] Loaders = [Community, Leaf, ZombieBuddy, None];
    internal static string LoaderName(string loader) => loader switch
    {
        Community => "PZLauncher · API 1", Leaf => "Leaf", ZombieBuddy => "ZombieBuddy", None => "no agent", _ => loader
    };
    internal static List<string> Selected(PlayerProfile profile) => profile.JavaLoader == Community ? profile.JavaModIds
        : profile.JavaModSelections.GetValueOrDefault(profile.JavaLoader) ?? [];
    internal static void SetSelected(PlayerProfile profile, IEnumerable<string> ids)
    {
        if (profile.JavaLoader == Community) profile.JavaModIds = ids.Distinct().ToList();
        else profile.JavaModSelections[profile.JavaLoader] = ids.Distinct().ToList();
    }
    internal static void SetOrder(PlayerProfile profile, IEnumerable<string> ids) =>
        profile.JavaModOrder[profile.JavaLoader] = ids.Distinct().ToList();
    internal static List<string> Requested(PlayerProfile profile, IEnumerable<JavaModEntry> catalog) => Selected(profile)
        .Concat(catalog.Where(m => m.Loader == profile.JavaLoader && m.Owner.Length > 0 && m.Owner != "@legacy" && m.OwnerEnabled)
            .Select(m => m.Id)).Distinct().OrderBy(id =>
            {
                int at = profile.JavaModOrder.GetValueOrDefault(profile.JavaLoader)?.IndexOf(id) ?? -1;
                return at < 0 ? int.MaxValue : at;
            }).ToList();
    private static string FileId(string text) => "jar-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..24];

    internal static List<JavaModEntry> Scan(PlayerProfile profile, IEnumerable<InstalledMod> traditional, IEnumerable<string> enabled, string installation, string side = "client")
    {
        string version = InstallationLocator.ReadGameVersion(installation)?.ToString() ?? "";
        var active = enabled.ToHashSet(StringComparer.Ordinal);
        var files = new Dictionary<string, JavaModEntry>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, string owner = "", bool ownerEnabled = true)
        {
            path = Path.GetFullPath(path);
            if (!files.ContainsKey(path))
            {
                var entry = Inspect(path, version, owner, side);
                files[path] = entry with { OwnerEnabled = ownerEnabled && (entry.Side is "both" or "unspecified" || entry.Side == side) };
            }
        }
        void DirectoryJars(string directory, string owner = "", bool ownerEnabled = true)
        {
            if (Directory.Exists(directory))
                foreach (string file in Directory.EnumerateFiles(directory, "*.jar")) Add(file, owner, ownerEnabled);
        }
        DirectoryJars(Folder(profile));
        foreach (var mod in traditional.Where(m => Directory.Exists(m.Location)))
        {
            bool on = mod.Available && active.Contains(mod.Id);
            string common = Path.Combine(mod.Location, "common");
            string? target = mod.Target.StartsWith('B') ? Path.Combine(mod.Location, mod.Target[1..]) : null;
            
            var layers = new List<string> { mod.Location, common };
            if (target != null) layers.Add(target);
            var bundled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string layer in layers)
                foreach (string relative in new[] { "java", "leaf/mods", "media/java" })
                {
                    string directory = Path.Combine(layer, relative);
                    if (Directory.Exists(directory))
                        foreach (string path in Directory.EnumerateFiles(directory, "*.jar"))
                            bundled[relative + "/" + Path.GetFileName(path)] = path;
                }
            foreach (string path in bundled.Values) Add(path, mod.Id, on);

            
            var lookups = new List<(string infoDirectory, string jarDirectory)>();
            if (target != null) lookups.Add((target, target));
            lookups.Add((common, common));
            if (target != null)
                lookups.Add(File.Exists(Path.Combine(common, "mod.info")) ? (common, target) : (target, common));
            else lookups.Add((mod.Location, mod.Location));
            JavaModEntry? buddy = null;
            bool declaredBuddy = false;
            foreach (var (infoDirectory, jarDirectory) in lookups)
            {
                string info = Path.Combine(infoDirectory, "mod.info");
                if (!File.Exists(info)) continue;
                var fields = ReadInfo(info);
                string jar = fields.GetValueOrDefault("javaJarFile", "");
                string package = fields.GetValueOrDefault("javaPkgName", "");
                if (jar.Length == 0 && package.Length == 0) continue;
                declaredBuddy = true;
                string id = FileId(mod.Id + "/zombiebuddy");
                string path = Path.GetFullPath(Path.Combine(jarDirectory, jar));
                string issue = package.Length == 0 || jar.Length == 0 ? T("java.buddyMetadata")
                    : jar.Replace('\\', '/').Contains("media/java/" + (side == "server" ? "client" : "server") + "/") ? T("java.sideMismatch", side == "server" ? "client" : "server", side)
                    : !File.Exists(path) ? T("mods.missing") : "";
                if (issue.Length == 0)
                {
                    try { using var archive = ZipFile.OpenRead(path); }
                    catch (Exception ex) when (ex is IOException or InvalidDataException) { issue = ex.Message; }
                }
                var candidate = new JavaModEntry(path, id, mod.Name, "", mod.Id, issue,
                    new() { Id = id, Name = mod.Name, Location = path, Available = issue.Length == 0 })
                    { Loader = ZombieBuddy, OwnerEnabled = on && !jar.Replace('\\', '/').Contains("media/java/" + (side == "server" ? "client" : "server") + "/"), Side = jar.Replace('\\', '/').Contains("media/java/client/") ? "client" : jar.Replace('\\', '/').Contains("media/java/server/") ? "server" : "both" };
                buddy ??= candidate;
                if (candidate.Available) { buddy = candidate; break; }
            }
            if (buddy != null) files[buddy.Path] = buddy;
            if (!declaredBuddy && mod.Requires.Any(id => id.Equals("ZombieBuddy", StringComparison.OrdinalIgnoreCase)))
            {
                string id = FileId(mod.Id + "/zombiebuddy");
                files[mod.Location + "/@zombiebuddy"] = new("", id, mod.Name, "", mod.Id, "",
                    new() { Id = id, Name = mod.Name, Available = true })
                    { Loader = ZombieBuddy, OwnerEnabled = on, DeclarationOnly = true };
            }
        }
        if (Directory.Exists(installation))
            foreach (string file in Directory.EnumerateFiles(installation, "*.jar").Where(p =>
                Path.GetFileName(p).StartsWith("pz-", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).StartsWith("epiclootz-", StringComparison.OrdinalIgnoreCase)))
                Add(file, "@legacy");
        var result = files.Values.ToList();
        foreach (string missing in Selected(profile).Where(id => result.All(m => m.Id != id || m.Loader != profile.JavaLoader)))
            result.Add(new("", missing, missing, "", "", T("mods.missing"),
                new() { Id = missing, Name = missing, Available = false }) { Loader = profile.JavaLoader });
        foreach (var group in result.Where(m => m.Available && (m.Owner.Length == 0 || m.Owner == "@legacy" || m.OwnerEnabled))
            .GroupBy(m => (m.Loader, m.Id)).Where(g => g.Count() > 1).ToList())
            foreach (var entry in group)
                result[result.IndexOf(entry)] = entry with { Issue = T("java.duplicate", entry.Id),
                    Metadata = new() { Id = entry.Id, Name = entry.Name, Available = false } };
        return result.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static Dictionary<string, string> ReadInfo(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith('#') || line.StartsWith(';')) continue;
            int at = line.IndexOf('=');
            if (at > 0) result.TryAdd(line[..at].Trim(), line[(at + 1)..].Trim());
        }
        return result;
    }
    private static string EntryText(ZipArchiveEntry entry)
    {
        if (entry.Length > 262144) throw new InvalidDataException(T("java.invalid"));
        using var input = new StreamReader(entry.Open(), Encoding.UTF8);
        return input.ReadToEnd();
    }
    internal static Dictionary<string, string> Manifest(ZipArchive archive)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (archive.GetEntry("META-INF/MANIFEST.MF") is not { } entry) return result;
        string? key = null;
        using var input = new StreamReader(entry.Open(), Encoding.UTF8);
        var lineBuffer = new StringBuilder(); int characters = 0;
        
        
        while (true)
        {
            int value = input.Read();
            if (++characters > 262144) throw new InvalidDataException(T("java.invalid"));
            if (value >= 0 && value != '\n') { lineBuffer.Append((char)value); continue; }
            string line = lineBuffer.ToString().TrimEnd('\r'); lineBuffer.Clear();
            if (line.Length == 0) break;
            if (line.StartsWith(' ') && key != null) { result[key] += line[1..]; continue; }
            int at = line.IndexOf(':');
            if (at > 0) { key = line[..at]; result[key] = line[(at + 1)..].Trim(); }
            if (value < 0) break;
        }
        return result;
    }
    internal static JavaModEntry Inspect(string path, string gameVersion, string owner = "", string side = "client")
    {
        if (side is not ("client" or "server")) throw new ArgumentException(nameof(side));
        string id = FileId(Path.GetFileName(path)), name = Path.GetFileNameWithoutExtension(path), version = "", loader = None, declaredSide = "unspecified";
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.GetEntry(Descriptor) != null) return InspectCommunity(path, gameVersion, owner, side);
            var manifest = Manifest(archive);
            declaredSide = manifest.GetValueOrDefault("PZ-Side", "unspecified");
            version = manifest.GetValueOrDefault("Implementation-Version", "");
            name = manifest.GetValueOrDefault("Implementation-Title", name);
            if (archive.GetEntry("leaf.mod.json") is { } descriptor)
            {
                loader = Leaf;
                using var json = JsonDocument.Parse(EntryText(descriptor));
                var root = json.RootElement;
                id = root.GetProperty("id").GetString() ?? "";
                name = root.TryGetProperty("name", out var title) ? title.GetString() ?? id : id;
                version = root.GetProperty("version").GetString() ?? "";
                if (!Regex.IsMatch(id, "^[a-z][a-z0-9_-]{1,63}$") || version.Length == 0 ||
                    !root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1)
                    throw new InvalidDataException(T("java.invalid"));
                if (id == "leafloader") throw new InvalidDataException(T("java.loaderNotMod"));
                declaredSide = root.TryGetProperty("environment", out var environment) ? environment.GetString() ?? "both" : "both";
                if (declaredSide == "*") declaredSide = "both";
                
            }
            else if (manifest.ContainsKey("Premain-Class") || manifest.ContainsKey("Agent-Class"))
            {
                loader = manifest.GetValueOrDefault("Premain-Class", "").Contains("zombie_buddy") ? ZombieBuddy : "agent";
                throw new InvalidDataException(T("java.loaderNotMod"));
            }
            else if (!archive.Entries.Any(e => e.FullName.EndsWith(".class", StringComparison.Ordinal)))
                throw new InvalidDataException(T("java.noClasses"));
            if (declaredSide is not ("client" or "server" or "both" or "unspecified")) throw new InvalidDataException(T("java.invalid"));
            if (declaredSide is "client" or "server" && declaredSide != side) throw new InvalidDataException(T("java.sideMismatch", declaredSide, side));
            return new(path, id, name, version, owner, "", new() { Id = id, Name = name, Location = path, Available = true }) { Loader = loader, Side = declaredSide };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new(path, id, name, version, owner, ex.Message, new() { Id = id, Name = name, Available = false }) { Loader = loader, Side = declaredSide };
        }
    }

    internal static IReadOnlyList<JavaModEntry> Compatible(PlayerProfile profile, IEnumerable<JavaModEntry> catalog) =>
        catalog.Where(m => m.Loader == profile.JavaLoader && (m.Owner.Length == 0 || m.Owner == "@legacy" || m.OwnerEnabled)).ToList();

    internal static string RuntimePath(PlayerProfile profile, string installation) => profile.JavaLoader switch
    {
        Leaf => profile.LeafLibraryPath.Length > 0 ? profile.LeafLibraryPath : Path.Combine(installation, ".leaf", "lib"),
        ZombieBuddy => profile.ZombieBuddyAgentPath.Length > 0 ? profile.ZombieBuddyAgentPath : Path.Combine(installation, "ZombieBuddy.jar"),
        _ => ""
    };

    internal static JavaLaunch Prepare(PlayerProfile profile, IReadOnlyList<JavaModEntry> catalog, string installation, string side = "client")
    {
        if (!Loaders.Contains(profile.JavaLoader)) throw new InvalidDataException(T("sharing.invalid"));
        var compatible = Compatible(profile, catalog);
        var resolved = ModResolver.Resolve(compatible.Select(m => m.Metadata), Requested(profile, catalog));
        if (!resolved.Success) throw new InvalidOperationException(string.Join("\n", resolved.Issues));
        if (profile.JavaLoader == Community) return PrepareCommunity(profile, compatible, installation, side);
        var selected = resolved.Ordered.Select(id => compatible.Single(m => m.Id == id && m.Available)).ToList();
        foreach (var mod in selected.Where(m => !m.DeclarationOnly))
        {
            if (!File.Exists(mod.Path)) throw new FileNotFoundException(T("mods.missing"), mod.Path);
            using var archive = ZipFile.OpenRead(mod.Path);
        }
        if (profile.JavaLoader == None)
            return new("", "", resolved.Ordered)
            {
                Loader = None, Classpath = selected.Select(m => Path.GetFullPath(m.Path)).ToList(),
                LooseClassesFirst = profile.DirectLooseClassesFirst,
                Report = T("java.directReport") + "\n" + CollisionReport(selected)
            };
        string runtime = RuntimePath(profile, installation);
        if (profile.JavaLoader == ZombieBuddy)
        {
            if (!File.Exists(runtime)) throw new FileNotFoundException(T("java.externalMissing", LoaderName(ZombieBuddy)), runtime);
            using var archive = ZipFile.OpenRead(runtime);
            string premain = Manifest(archive).GetValueOrDefault("Premain-Class", "");
            if (premain is not ("me.zed_0xff.zombie_buddy.Agent" or "zombiebuddy.Agent") ||
                archive.GetEntry(premain.Replace('.', '/') + ".class") == null)
                throw new InvalidDataException(T("java.wrongRuntime", LoaderName(ZombieBuddy)));
            return new(Path.GetFullPath(runtime), "", resolved.Ordered)
            {
                Loader = ZombieBuddy, Report = T("java.buddyReport"),
                
                VmArguments = ["-Djava.awt.headless=" + (side == "server" ? "true" : "false"), "-Dserver=" + (side == "server" ? "true" : "false")]
            };
        }
        string directory = Directory.Exists(runtime) ? runtime : Path.GetDirectoryName(Path.GetFullPath(runtime))!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(T("java.externalMissing", "Leaf"));
        var libraries = Directory.EnumerateFiles(directory, "*.jar", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToList();
        string main = "dev.aoqia.leaf.loader.impl.launch.knot.Knot" + (side == "server" ? "Server" : "Client");
        var required = new[] { main.Replace('.', '/') + ".class", "org/objectweb/asm/ClassReader.class",
            "org/objectweb/asm/tree/ClassNode.class", "org/objectweb/asm/commons/Remapper.class",
            "org/objectweb/asm/tree/analysis/Analyzer.class", "org/objectweb/asm/util/CheckClassAdapter.class",
            "org/spongepowered/asm/launch/MixinBootstrap.class" };
        var found = new HashSet<string>(StringComparer.Ordinal);
        int loaderCount = 0;
        foreach (string path in libraries)
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.GetEntry(required[0]) != null) loaderCount++;
            foreach (string entry in required) if (archive.GetEntry(entry) != null) found.Add(entry);
        }
        if (loaderCount != 1 || required.Any(entry => !found.Contains(entry)))
            throw new InvalidDataException(T("java.leafLibraries"));
        string manifestPath = Path.Combine(LauncherStorage.Root, "runtime", "profiles", profile.Id, "leaf-mods.txt");
        LauncherStorage.WriteAtomic(manifestPath, string.Join("\n", selected.Where(m => !m.DeclarationOnly).Select(m => Path.GetFullPath(m.Path))) + "\n");
        
        
        var disabled = catalog.Where(m => m.Loader == Leaf && !resolved.Ordered.Contains(m.Id)).Select(m => m.Id).Distinct();
        return new("", "", resolved.Ordered)
        {
            Loader = Leaf, Classpath = libraries, MainClass = main, Report = T("java.leafReport"),
            VmArguments = ["-Dleaf.addMods=@" + manifestPath, "-Dleaf.gamePath=" + Path.GetFullPath(installation),
                "-Dleaf.disableWorkshopMods=true", "-Dleaf.debug.disableModIds=" + string.Join(",", disabled)]
        };
    }
    private static string CollisionReport(IEnumerable<JavaModEntry> selected)
    {
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicts = new List<string>();
        foreach (var mod in selected)
        {
            using var archive = ZipFile.OpenRead(mod.Path);
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".class")))
            {
                if (!classes.TryAdd(entry.FullName, mod.Name))
                    conflicts.Add(entry.FullName + ": " + classes[entry.FullName] + " > " + mod.Name);
            }
        }
        return conflicts.Count == 0 ? "" : T("java.collisions") + "\n" + string.Join("\n", conflicts);
    }
}
