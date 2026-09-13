using System.IO.Compression;
using System.Text.Json;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class RuntimeClients
{
    internal static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    internal static bool SameCache(string a, string b) => Path.GetFullPath(a).TrimEnd('\\', '/').Equals(Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    internal static void EnsureIsolated(string cache, string other)
    {
        string a = Path.GetFullPath(cache).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, b = Path.GetFullPath(other).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        if (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase)) throw new IOException(T("runtime.cacheShared"));
    }
    internal static void Relocate(PlayerProfile profile, string oldCache, string newCache)
    {
        string Move(string path) => path.Length > 0 && Path.GetFullPath(path).StartsWith(Path.GetFullPath(oldCache).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? WorldFiles.Within(newCache, Path.GetRelativePath(oldCache, path)) : path;
        profile.LeafLibraryPath = Move(profile.LeafLibraryPath); profile.ZombieBuddyAgentPath = Move(profile.ZombieBuddyAgentPath); profile.CachePath = newCache;
    }
    internal static PlayerProfile Create(ServerPreset server, string installation, string name, string parent, PlayerProfile? template = null)
    {
        ServerRuntimePack? pack = server.RuntimePackSha256.Length > 0 ? ServerRuntimePack.VerifyInstalled(server, installation) : null;
        var p = new PlayerProfile { Name = name, DedicatedServerId = server.Id, Steam = server.Steam, AutomaticJvm = true, JavaLoader = JavaMods.None };
        p.CachePath = WorldFiles.Within(parent, p.Id); EnsureIsolated(p.CachePath, server.CachePath); Directory.CreateDirectory(p.CachePath);
        if (template != null && File.Exists(Path.Combine(template.CachePath, "options.ini"))) ServerRuntimePack.Copy(Path.Combine(template.CachePath, "options.ini"), Path.Combine(p.CachePath, "options.ini"));
        if (pack != null) Install(p, ServerRuntimePack.Manifest(server), installation);
        SetConnection(p, server); return p;
    }
    internal static void Install(PlayerProfile p, string manifest, string installation)
    {
        var pack = ServerRuntimePack.Read(manifest, installation); string source = Path.GetDirectoryName(manifest)!;
        foreach (var file in pack.Files)
        {
            ServerRuntimePack.Copy(WorldFiles.Within(source, file.Path), WorldFiles.Within(p.CachePath, "runtime-pack/" + file.Path));
            if (file.Kind == "mod") ServerRuntimePack.Copy(WorldFiles.Within(source, file.Path), WorldFiles.Within(p.CachePath, file.Path));
        }
        ServerRuntimePack.Copy(manifest, ServerRuntimePack.Manifest(p));
        p.RuntimePackSha256 = WorldFiles.Hash(manifest); p.RuntimePackId = pack.Id; p.RuntimePackVersion = pack.Version; p.Launch.ModFolders = "mods";
        var selection = new ModSelection(p.CachePath); selection.Ids.Clear(); selection.Ids.AddRange(pack.Mods.Select(m => m.Id)); selection.Save();
        VerifyFiles(p, installation);
    }
    internal static ServerRuntimePack VerifyFiles(PlayerProfile p, string installation)
    {
        var pack = ServerRuntimePack.VerifyFiles(p.CachePath, p.RuntimePackSha256, installation);
        if (pack.Id != p.RuntimePackId || pack.Version != p.RuntimePackVersion) throw new InvalidDataException(T("runtime.packDifferent"));
        if (p.Launch.ModFolders != "mods" || p.DirectLooseClassesFirst) throw new InvalidDataException(T("runtime.loadOrder"));
        ServerRuntimePack.VerifyMods(pack, p.CachePath, ModCatalog.Scan(p, InstallationLocator.ReadGameVersion(installation)), new ModSelection(p.CachePath).Ids);
        return pack;
    }
    internal static ServerPreset VerifyBinding(LauncherState state, PlayerProfile p, string installation)
    {
        var server = state.Servers.SingleOrDefault(s => s.Id == p.DedicatedServerId) ?? throw new InvalidDataException(T("runtime.serverMissing"));
        EnsureIsolated(p.CachePath, server.CachePath);
        foreach (var other in state.Profiles.Where(other => other.Id != p.Id && other.DedicatedServerId == server.Id)) EnsureIsolated(p.CachePath, other.CachePath);
        if (!ServerRuntimePack.HashEquals(server.RuntimePackSha256, p.RuntimePackSha256)) throw new InvalidDataException(T("runtime.packDifferent"));
        if (p.Steam != server.Steam) throw new InvalidDataException(T("runtime.steamDifferent"));
        if (p.RuntimePackSha256.Length > 0)
        {
            VerifyFiles(p, installation); ServerRuntimePack.VerifyInstalled(server, installation);
        }
        return server;
    }
    internal static void Associate(LauncherState state, PlayerProfile p, ServerPreset server, string installation)
    {
        if (p.OnlineServerId.Length > 0) throw new InvalidDataException(T("runtime.onlineProfile"));
        var copy = Clone(p); copy.DedicatedServerId = server.Id; VerifyBinding(state, copy, installation);
        GameProcessGuard.EnsureStopped(p.CachePath); p.DedicatedServerId = server.Id; SetConnection(p, server);
    }
    internal static void SetConnection(PlayerProfile p, ServerPreset server)
    {
        string host = server.ClientAddress.Trim();
        if (host.Length == 0 || host.Length > 253 || host.IndexOfAny(['/', '\\', ':', ' ', '\r', '\n', '\0']) >= 0 || Uri.CheckHostName(host) == UriHostNameType.Unknown) throw new InvalidDataException(T("runtime.addressInvalid"));
        var ini = ServerFiles.IniValues(File.ReadAllText(server.Ini));
        if (!int.TryParse(ini.GetValueOrDefault("DefaultPort", "16261"), out int port) || port is < 1 or > 65535) throw new InvalidDataException(T("server.port", "DefaultPort"));
        p.Launch.ConnectAddress = host + ":" + port; p.Launch.ConnectPassword = ini.GetValueOrDefault("Password", "");
    }
    internal static LaunchPlan Attach(LaunchPlan plan, PlayerProfile p, string installation)
    {
        if (p.RuntimePackSha256.Length == 0) return plan;
        var pack = VerifyFiles(p, installation); var overlays = pack.JavaClasspath(Path.GetDirectoryName(ServerRuntimePack.Manifest(p))!, "client");
        var args = plan.Arguments.ToList(); int cp = args.IndexOf("-cp");
        if (args.Any(a => a.StartsWith("-javaagent:") || a.StartsWith("-agentpath:") || a.StartsWith("-agentlib:") || a.StartsWith("-Xbootclasspath") || a.StartsWith("-Djava.system.class.loader="))) throw new InvalidDataException(T("runtime.loadOrder"));
        if (cp < 0 || args[cp + 2] != "zombie.gameStates.MainScreenState") throw new InvalidDataException(T("runtime.loadOrder"));
        args[cp + 1] = string.Join(';', overlays.Concat(["projectzomboid.jar", "."]));
        args.InsertRange(0, ["-Dpzlauncher.runtimePack=" + pack.Id + "@" + pack.Version, "-Dpzlauncher.runtimePackSha256=" + p.RuntimePackSha256, "-Dpzlauncher.runtimeSide=client"]);
        return new() { Arguments = args, JavaExecutable = plan.JavaExecutable, WorkingDirectory = plan.WorkingDirectory, CacheDirectory = plan.CacheDirectory, ConsoleLogPath = plan.ConsoleLogPath };
    }
    internal static void CheckOverlayConflicts(IEnumerable<string> overlays, JavaLaunch java)
    {
        var paths = overlays.ToList(); if (java.Loader == JavaMods.None) paths.AddRange(java.Classpath);
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            using var jar = ZipFile.OpenRead(path);
            foreach (var entry in jar.Entries.Where(e => e.FullName.EndsWith(".class", StringComparison.Ordinal)))
                if (!classes.TryAdd(entry.FullName, path)) throw new InvalidDataException(T("pack.overlap", entry.FullName, classes[entry.FullName], path));
        }
    }
}
