using System.IO.Compression;
using System.Text.Json;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class ServerJavaVerification
{
    internal static void Jar(string path, params (string Name, string Text)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open()); writer.Write(entry.Text); }
    }
    internal static string Pack(string root, string installation, string version)
    {
        string folder = Path.Combine(root, "generic-" + version); Directory.CreateDirectory(folder);
        var pack = new ServerRuntimePack { Format = 1, Id = "GenericRuntime", Version = version, GameVersion = "42.20", GameJarSha256 = WorldFiles.Hash(Path.Combine(installation, "projectzomboid.jar")), Mods = [new() { Id = "GenericRuntime", Folder = "GenericRuntime" }] };
        foreach (var (path, side) in new[] { ("java/common.jar", "both"), ("java/client.jar", "client"), ("java/server.jar", "server") })
        { Jar(WorldFiles.Within(folder, path), ("fixture/" + side + ".class", version)); pack.Files.Add(new() { Path = path, Side = side, Kind = "java", Sha256 = WorldFiles.Hash(WorldFiles.Within(folder, path)) }); }
        string info = WorldFiles.Within(folder, "mods/GenericRuntime/42/mod.info"); Directory.CreateDirectory(Path.GetDirectoryName(info)!); File.WriteAllText(info, "id=GenericRuntime\nname=Generic fixture\n");
        pack.Files.Add(new() { Path = "mods/GenericRuntime/42/mod.info", Kind = "mod", Side = "both", Sha256 = WorldFiles.Hash(info) });
        string manifest = Path.Combine(folder, ServerRuntimePack.ManifestName); File.WriteAllText(manifest, JsonSerializer.Serialize(pack, ServerRuntimePack.Json)); return manifest;
    }
    internal static void Run(string root, Action<string, Action> check)
    {
        string directory = Path.Combine(root, "server-java"); Directory.CreateDirectory(directory); string installation = InstallationLocator.Find("");
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Reject(Action action) { bool rejected = false; try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException) { rejected = true; } Assert(rejected, "Invalid setup accepted"); }
        check("Exécution de contrôle : un cache inattendu interdit tout démarrage du jeu", () =>
        {
            if (Environment.GetEnvironmentVariable("PZLAUNCHER_DATA") != null) Assert(RuntimeClients.SameCache(LauncherStorage.DefaultGameData, Path.Combine(LauncherStorage.Root, "game-data")), "Isolated launcher defaults to the user's game cache.");
            string output = Path.Combine(directory, "cache-guard");
            using var window = new LauncherWindow(verification: true, verifyLaunchDirectory: output, expectedLaunchCache: Path.Combine(directory, "must-not-fall-back"));
            window.VerifyLaunchCacheGuard();
            Assert(File.ReadAllText(Path.Combine(output, "error.txt")).Contains("unexpected client cache") && !File.Exists(Path.Combine(output, "launch.json")), "Unexpected cache was not rejected before launch.");
        });
        check("Java serveur : métadonnées de rôle API, Leaf et patchs directs", () =>
        {
            foreach (string side in new[] { "server", "client", "both" })
            {
                string api = Path.Combine(directory, "api-" + side + ".jar"), leaf = Path.Combine(directory, "leaf-" + side + ".jar"), direct = Path.Combine(directory, "direct-" + side + ".jar");
                Jar(api, (JavaMods.Descriptor, "api=1\nid=fixture_" + side + "\nversion=1\nentrypoint=fixture.Mod\ngameVersions=42.20\nside=" + side));
                Jar(leaf, ("leaf.mod.json", "{\"schemaVersion\":1,\"id\":\"fixture_" + side + "\",\"version\":\"1\",\"environment\":\"" + side + "\"}"));
                Jar(direct, ("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\nPZ-Side: " + side + "\n\n"), ("fixture/Direct.class", "fixture"));
                foreach (string path in new[] { api, leaf, direct }) foreach (string requested in new[] { "client", "server" }) Assert(JavaMods.Inspect(path, "42.20", side: requested).Available == (side == "both" || side == requested), path + " wrong side");
            }
        });
        check("Java serveur : Leaf démarre KnotServer et conserve GameServer sans Leaf", () =>
        {
            var server = new ServerPreset { Name = "generic-leaf", CachePath = Path.Combine(directory, "leaf-cache"), Steam = false }; ServerFiles.Create(server);
            var p = server.JavaProfile(); p.JavaLoaderEnabled = true; p.JavaLoader = JavaMods.Leaf; p.LeafLibraryPath = Path.Combine(directory, "leaf-libraries");
            Jar(Path.Combine(p.LeafLibraryPath, "runtime.jar"), new[] { "dev/aoqia/leaf/loader/impl/launch/knot/KnotServer.class", "dev/aoqia/leaf/loader/impl/launch/knot/KnotClient.class", "org/objectweb/asm/ClassReader.class", "org/objectweb/asm/tree/ClassNode.class", "org/objectweb/asm/commons/Remapper.class", "org/objectweb/asm/tree/analysis/Analyzer.class", "org/objectweb/asm/util/CheckClassAdapter.class", "org/spongepowered/asm/launch/MixinBootstrap.class" }.Select(n => (n, "fixture")).ToArray());
            var plan = ServerFiles.Plan(installation, server); int cp = plan.Arguments.ToList().IndexOf("-cp"); Assert(plan.Arguments[cp + 2].EndsWith("KnotServer"), "Leaf client launched on server");
            Assert(plan.Arguments.Contains("-servername") && !plan.Arguments.Contains("-coop"), "Dedicated arguments lost");
            var vanilla = JavaMods.Attach(plan, new("", "", []) { Loader = JavaMods.None }); cp = vanilla.Arguments.ToList().IndexOf("-cp");
            Assert(vanilla.Arguments[cp + 2] == "zombie.network.GameServer", "Leaf fallback started client");
        });
        check("Java serveur : ZombieBuddy isServer, headless et séparation des JAR liés", () =>
        {
            string cache = Path.Combine(directory, "buddy-cache"), owner = Path.Combine(cache, "mods", "GenericBuddy", "42"); Directory.CreateDirectory(owner);
            File.WriteAllText(Path.Combine(owner, "mod.info"), "id=GenericBuddy\njavaJarFile=media/java/server/patch.jar\njavaPkgName=fixture\n");
            Jar(Path.Combine(owner, "media", "java", "server", "patch.jar"), ("fixture/Patch.class", "fixture"));
            var p = new PlayerProfile { CachePath = cache, JavaLoader = JavaMods.ZombieBuddy, ZombieBuddyAgentPath = Path.Combine(cache, "ZombieBuddy.jar") };
            Jar(p.ZombieBuddyAgentPath, ("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\nPremain-Class: me.zed_0xff.zombie_buddy.Agent\n\n"), ("me/zed_0xff/zombie_buddy/Agent.class", "fixture"));
            var mods = new[] { new InstalledMod { Id = "GenericBuddy", Name = "GenericBuddy", Location = Path.GetDirectoryName(owner)!, Target = "B42", Available = true } };
            var client = JavaMods.Scan(p, mods, ["GenericBuddy"], installation);
            var server = JavaMods.Scan(p, mods, ["GenericBuddy"], installation, "server");
            Assert(client.Single(m => m.Owner == "GenericBuddy").OwnerEnabled == false && server.Single(m => m.Owner == "GenericBuddy").Available, "Buddy role filtering");
            var java = JavaMods.Prepare(p, server, installation, "server"); Assert(java.VmArguments.Contains("-Dserver=true") && java.VmArguments.Contains("-Djava.awt.headless=true"), "Server frontend mode lost");
        });
        string v1 = Pack(directory, installation, "1"), v2 = Pack(directory, installation, "2");
        var dedicated = ServerRuntimePack.Create(v1, installation, "generic-pack", Path.Combine(directory, "servers"), false);
        var clientProfile = RuntimeClients.Create(dedicated, installation, "Generic client", Path.Combine(directory, "clients"));
        var state = new LauncherState { Installation = installation, Profiles = [clientProfile], Servers = [dedicated], SelectedProfile = clientProfile.Id };
        check("Runtime générique : rôles Java séparés, caches indépendants et persistance des liaisons", () =>
        {
            var serverPlan = ServerFiles.Plan(installation, dedicated);
            var plan = RuntimeClients.Attach(new GameLaunchService().CreateLaunchPlan(installation, clientProfile.AsGameProfile(), clientProfile.AsJvmProfile()), clientProfile, installation);
            string clientCp = plan.Arguments[plan.Arguments.ToList().IndexOf("-cp") + 1], serverCp = serverPlan.Arguments[serverPlan.Arguments.ToList().IndexOf("-cp") + 1];
            Assert(clientCp.Contains("client.jar") && !clientCp.Contains("server.jar") && serverCp.Contains("server.jar") && !serverCp.Contains("client.jar"), "Role contamination");
            Assert(clientCp.EndsWith("projectzomboid.jar;.") && plan.Arguments.Contains("+connect") && plan.Arguments.Contains("127.0.0.1:16261"), "Client command mismatch");
            var saved = RuntimeClients.Clone(state); RuntimeClients.VerifyBinding(saved, saved.Profiles[0], installation);
            Assert(saved.Profiles[0].Id == clientProfile.Id && saved.Servers[0].Id == dedicated.Id && saved.Profiles[0].CachePath != dedicated.CachePath, "Persistence changed identity/cache");
        });
        check("Runtime : mauvaise identité, fichier altéré, cache partagé et classes concurrentes refusés", () =>
        {
            var p = RuntimeClients.Clone(clientProfile); p.RuntimePackSha256 = new string('0', 64); Reject(() => RuntimeClients.VerifyBinding(state, p, installation));
            p = RuntimeClients.Clone(clientProfile); p.CachePath = dedicated.CachePath; Reject(() => RuntimeClients.VerifyBinding(state, p, installation));
            p = RuntimeClients.Clone(clientProfile); p.Id = Guid.NewGuid().ToString("N"); Reject(() => RuntimeClients.VerifyBinding(state, p, installation));
            p = RuntimeClients.Clone(clientProfile); p.Launch.ModFolders = "steam,mods"; Reject(() => RuntimeClients.VerifyBinding(state, p, installation));
            string info = WorldFiles.Within(clientProfile.CachePath, "mods/GenericRuntime/42/mod.info"); string before = File.ReadAllText(info); File.AppendAllText(info, "changed"); Reject(() => RuntimeClients.VerifyBinding(state, clientProfile, installation)); File.WriteAllText(info, before);
            string overlay = WorldFiles.Within(clientProfile.CachePath, "runtime-pack/java/common.jar"); Reject(() => RuntimeClients.CheckOverlayConflicts([overlay], new("", "", []) { Loader = JavaMods.None, Classpath = [overlay] }));
        });
        check("Runtime : échec de publication conserve toutes les références de l'ancienne génération", () =>
        {
            string original = JsonSerializer.Serialize(state); string pin = dedicated.RuntimePackSha256;
            Reject(() => RuntimeRevisions.Replace(state, dedicated, v2, installation, Path.Combine(directory, "failed-update"), _ => throw new IOException("Injected persistence failure")));
            Assert(JsonSerializer.Serialize(state) == original && dedicated.RuntimePackSha256 == pin, "Partial state became visible"); RuntimeClients.VerifyBinding(state, clientProfile, installation);
        });
        check("Runtime : remplacement du groupe, sauvegardes, données conservées et retour complet", () =>
        {
            File.WriteAllText(Path.Combine(clientProfile.CachePath, "options.ini"), "userSetting=7\n");
            string world = WorldFiles.Within(dedicated.CachePath, "Saves/Multiplayer/" + dedicated.Name); Directory.CreateDirectory(world); File.WriteAllText(Path.Combine(world, "fixture.dat"), "before");
            string stateFile = Path.Combine(directory, "state.json"); void Persist(LauncherState s) => LauncherStorage.WriteAtomic(stateFile, JsonSerializer.Serialize(s));
            var current = RuntimeRevisions.Replace(state, dedicated, v2, installation, Path.Combine(directory, "updates"), Persist);
            Assert(current.CachePath != dedicated.CachePath && state.Profiles[0].RuntimePackVersion == "2" && state.RuntimeRevisions.Count == 1, "New generation not installed coherently");
            Assert(File.ReadAllText(Path.Combine(state.Profiles[0].CachePath, "options.ini")) == "userSetting=7\n", "Client settings lost");
            File.WriteAllText(Path.Combine(current.World, "fixture.dat"), "after");
            var restored = RuntimeRevisions.Rollback(state, current, installation, Path.Combine(directory, "updates"), Persist);
            Assert(restored.RuntimePackSha256 == dedicated.RuntimePackSha256 && state.Profiles[0].RuntimePackVersion == "1" && File.ReadAllText(Path.Combine(restored.World, "fixture.dat")) == "before", "Rollback mixed runtime or world generations");
            Assert(File.ReadAllText(Path.Combine(current.World, "fixture.dat")) == "after", "Previous generation was overwritten");
            var reread = JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(stateFile))!; RuntimeClients.VerifyBinding(reread, reread.Profiles[0], installation);
        });
        check("Java serveur : paramètres Java et JAR locaux suivent la sauvegarde/restauration", () =>
        {
            var server = new ServerPreset { Name = "free-jars", CachePath = Path.Combine(directory, "free-server"), Steam = false }; ServerFiles.Create(server);
            var p = server.JavaProfile(); p.JavaLoader = JavaMods.None; p.JavaLoaderEnabled = true;
            string jar = Path.Combine(JavaMods.Folder(p), "generic-server.jar"); Jar(jar, ("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\nPZ-Side: server\n\n"), ("fixture/Standalone.class", "fixture"));
            var item = JavaMods.Inspect(jar, "42.20", side: "server"); JavaMods.SetSelected(p, [item.Id]);
            var restored = ServerFiles.Restore(ServerFiles.Backup(server, Path.Combine(directory, "free-backup")), Path.Combine(directory, "free-restore"));
            Assert(restored.Id != server.Id && restored.Java.Id != p.Id, "Restored server shares its Java launch manifest with the source.");
            var plan = ServerFiles.Plan(installation, restored); Assert(plan.Arguments.Any(a => a.Contains(restored.CachePath) && a.Contains("generic-server.jar")), "Restored Java points to old cache");
        });
    }
}
