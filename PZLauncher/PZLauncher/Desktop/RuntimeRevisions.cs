using System.Text.Json;

namespace PZLauncher.Desktop;

internal sealed class RuntimeClientSnapshot
{
    public PlayerProfile Profile { get; set; } = new();
    public string Archive { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
internal sealed class RuntimeRevision
{
    public string ServerId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string ActiveHash { get; set; } = "";
    public string ServerArchive { get; set; } = "";
    public string ServerArchiveSha256 { get; set; } = "";
    public List<RuntimeClientSnapshot> Clients { get; set; } = [];
}
internal static class RuntimeRevisions
{
    private static void Stopped(LauncherState state, ServerPreset server)
    {
        GameProcessGuard.EnsureStopped(server.CachePath);
        foreach (var p in state.Profiles.Where(p => p.DedicatedServerId == server.Id))
        { RuntimeClients.EnsureIsolated(p.CachePath, server.CachePath); GameProcessGuard.EnsureStopped(p.CachePath); }
    }
    private static RuntimeRevision Snapshot(LauncherState state, ServerPreset server, string parent)
    {
        Stopped(state, server);
        var revision = new RuntimeRevision { ServerId = server.Id, ServerArchive = ServerFiles.Backup(server, Path.Combine(parent, "backups")) };
        revision.ServerArchiveSha256 = WorldFiles.Hash(revision.ServerArchive);
        foreach (var p in state.Profiles.Where(p => p.DedicatedServerId == server.Id))
        {
            string archive = WorldFiles.Backup(p.CachePath, Path.Combine(parent, "backups"));
            revision.Clients.Add(new() { Profile = RuntimeClients.Clone(p), Archive = archive, Sha256 = WorldFiles.Hash(archive) });
        }
        return revision;
    }
    private static void RestoreFiles(string cache, ServerRuntimePack oldPack)
    {
        
        foreach (string relative in oldPack.Mods.Select(m => "mods/" + m.Folder).Append("runtime-pack"))
        {
            string path = WorldFiles.Within(cache, relative);
            if (!Directory.Exists(path)) continue;
            WorldFiles.Enumerate(path); Directory.Delete(path, true);
        }
    }
    private static void InstallServer(ServerPreset next, string sourceManifest, ServerRuntimePack pack)
    {
        string root = Path.GetDirectoryName(sourceManifest)!;
        foreach (var file in pack.Files)
        {
            ServerRuntimePack.Copy(WorldFiles.Within(root, file.Path), WorldFiles.Within(next.CachePath, "runtime-pack/" + file.Path));
            if (file.Kind == "mod") ServerRuntimePack.Copy(WorldFiles.Within(root, file.Path), WorldFiles.Within(next.CachePath, file.Path));
        }
        ServerRuntimePack.Copy(sourceManifest, ServerRuntimePack.Manifest(next)); next.RuntimePackSha256 = WorldFiles.Hash(sourceManifest);
    }
    private static void Validate(LauncherState candidate, ServerPreset server, string installation)
    {
        _ = ServerFiles.Plan(installation, server);
        foreach (var p in candidate.Profiles.Where(p => p.DedicatedServerId == server.Id))
        {
            RuntimeClients.VerifyBinding(candidate, p, installation); RuntimeClients.SetConnection(p, server);
            var plan = new PZLauncher.Services.GameLaunchService().CreateLaunchPlan(installation, p.AsGameProfile(), p.AsJvmProfile());
            plan = RuntimeClients.Attach(plan, p, installation);
            if (p.JavaLoaderEnabled)
            {
                var java = JavaMods.Prepare(p, JavaMods.Scan(p, ModCatalog.Scan(p, InstallationLocator.ReadGameVersion(installation)), new ModSelection(p.CachePath).Ids, installation), installation);
                RuntimeClients.CheckOverlayConflicts(plan.Arguments[plan.Arguments.ToList().IndexOf("-cp") + 1].Split(';').Where(Path.IsPathRooted), java);
                _ = JavaMods.Attach(plan, java);
            }
        }
    }
    private static void Commit(LauncherState state, LauncherState next, Action<LauncherState> persist)
    {
        persist(next); 
        state.Servers = next.Servers; state.Profiles = next.Profiles; state.RuntimeRevisions = next.RuntimeRevisions;
    }
    internal static ServerPreset Replace(LauncherState state, ServerPreset server, string manifest, string installation, string parent, Action<LauncherState>? persist = null)
    {
        var old = ServerRuntimePack.VerifyInstalled(server, installation); var pack = ServerRuntimePack.Read(manifest, installation);
        if (pack.Id != old.Id || ServerRuntimePack.HashEquals(WorldFiles.Hash(manifest), server.RuntimePackSha256)) throw new InvalidDataException(T("runtime.replacementInvalid"));
        foreach (var p in state.Profiles.Where(p => p.DedicatedServerId == server.Id)) RuntimeClients.VerifyBinding(state, p, installation);
        var revision = Snapshot(state, server, parent);
        var next = RuntimeClients.Clone(state);
        var newServer = ServerFiles.Restore(revision.ServerArchive, Path.Combine(parent, "server-generations")); newServer.Id = server.Id;
        RestoreFiles(newServer.CachePath, old); InstallServer(newServer, manifest, pack);
        var values = ServerFiles.Read(newServer); var edited = new Dictionary<string, string>(values);
        var ids = newServer.EnabledMods().Except(old.Mods.Select(m => m.Id)).Concat(pack.Mods.Select(m => m.Id)).Distinct();
        edited[".ini"] = ServerFiles.UpdateValue(edited[".ini"], "Mods", string.Join(';', ids.Select(id => "\\" + id))); ServerFiles.Save(newServer, values, edited);
        next.Servers[next.Servers.FindIndex(s => s.Id == server.Id)] = newServer;
        foreach (var snapshot in revision.Clients)
        {
            var p = RuntimeClients.Clone(snapshot.Profile); string cache = WorldFiles.RestoreAsCopy(snapshot.Archive, Path.Combine(parent, "client-generations"));
            RuntimeClients.Relocate(p, p.CachePath, cache); RestoreFiles(cache, old);
            var previousIds = new ModSelection(cache).Ids.Except(old.Mods.Select(m => m.Id)).ToList();
            RuntimeClients.Install(p, manifest, installation);
            var selection = new ModSelection(cache); selection.Ids.AddRange(previousIds.Except(selection.Ids)); selection.Save();
            next.Profiles[next.Profiles.FindIndex(c => c.Id == p.Id)] = p;
        }
        Validate(next, newServer, installation); Stopped(state, server);
        revision.ActiveHash = newServer.RuntimePackSha256; next.RuntimeRevisions.Add(revision);
        Commit(state, next, persist ?? LauncherStorage.Save); return newServer;
    }
    internal static ServerPreset Rollback(LauncherState state, ServerPreset server, string installation, string parent, Action<LauncherState>? persist = null)
    {
        var revision = state.RuntimeRevisions.LastOrDefault(r => r.ServerId == server.Id) ?? throw new IOException(T("runtime.noPrevious"));
        if (!ServerRuntimePack.HashEquals(revision.ActiveHash, server.RuntimePackSha256)) throw new IOException(T("runtime.packDifferent"));
        if (!ServerRuntimePack.HashEquals(WorldFiles.Hash(revision.ServerArchive), revision.ServerArchiveSha256) || revision.Clients.Any(c => !ServerRuntimePack.HashEquals(WorldFiles.Hash(c.Archive), c.Sha256))) throw new IOException(T("world.changed"));
        var linked = state.Profiles.Where(p => p.DedicatedServerId == server.Id).ToArray();
        if (!linked.Select(p => p.Id).ToHashSet().SetEquals(revision.Clients.Select(c => c.Profile.Id))) throw new IOException(T("runtime.clientSetChanged"));
        var safety = Snapshot(state, server, parent);
        File.WriteAllText(Path.Combine(parent, "rollback-safety-" + Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(safety, ServerRuntimePack.Json));
        var next = RuntimeClients.Clone(state); var restored = ServerFiles.Restore(revision.ServerArchive, Path.Combine(parent, "server-generations")); restored.Id = server.Id;
        next.Servers[next.Servers.FindIndex(s => s.Id == server.Id)] = restored;
        foreach (var snapshot in revision.Clients)
        {
            var p = RuntimeClients.Clone(snapshot.Profile); RuntimeClients.Relocate(p, p.CachePath, WorldFiles.RestoreAsCopy(snapshot.Archive, Path.Combine(parent, "client-generations")));
            next.Profiles[next.Profiles.FindIndex(c => c.Id == p.Id)] = p;
        }
        next.RuntimeRevisions.RemoveAt(next.RuntimeRevisions.FindLastIndex(r => r.ServerId == server.Id));
        Validate(next, restored, installation); Stopped(state, server); Commit(state, next, persist ?? LauncherStorage.Save); return restored;
    }
}
