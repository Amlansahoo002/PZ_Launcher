using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace PZLauncher.Desktop;

internal static class RuntimePackVerification
{
    internal static void Run(string root, Action<string, Action> check)
    {
        string installation = InstallationLocator.Find("");
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Reject(Action action)
        {
            bool rejected = false; try { action(); } catch (Exception e) when (e is IOException or InvalidDataException) { rejected = true; }
            Assert(rejected, "Invalid runtime accepted.");
        }
        (string Manifest, ServerRuntimePack Pack) Fixture()
        {
            string folder = Path.Combine(root, "pack-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var pack = new ServerRuntimePack { Format = 1, Id = "Fixture", Version = "1", GameVersion = "42.20", GameJarSha256 = WorldFiles.Hash(Path.Combine(installation, "projectzomboid.jar")), Mods = [new() { Id = "PZLauncherRuntimeFixture", Folder = "Fixture" }] };
            void Add(string relative, string kind, string side, string text)
            {
                string path = WorldFiles.Within(folder, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (kind == "java") { using var zip = ZipFile.Open(path, ZipArchiveMode.Create); using var writer = new StreamWriter(zip.CreateEntry(text + ".class").Open()); writer.Write("fixture-only"); }
                else File.WriteAllText(path, text);
                pack.Files.Add(new() { Path = relative, Kind = kind, Side = side, Sha256 = WorldFiles.Hash(path) });
            }
            Add("java/common.jar", "java", "both", "fixture/Common"); Add("java/renderer.jar", "java", "client", "fixture/Renderer");
            Add("mods/Fixture/42/mod.info", "mod", "both", "name=Runtime fixture\nid=PZLauncherRuntimeFixture\nversionMin=42.20.0\n");
            Add("mods/Fixture/42/media/lua/server/fixture.lua", "mod", "both", "-- fixture\n");
            string manifest = Path.Combine(folder, ServerRuntimePack.ManifestName); Write(manifest, pack); return (manifest, pack);
        }
        ServerPreset Import(string manifest) => ServerRuntimePack.Create(manifest, installation, "pack-test", Path.Combine(root, "pack-caches"), false);
        check("Pack : création dédiée, copie autonome et séparation Java client/serveur", () =>
        {
            var (manifest, pack) = Fixture(); var preset = Import(manifest);
            File.Delete(manifest); 
            var plan = ServerFiles.Plan(installation, preset, "test-secret"); int cp = plan.Arguments.ToList().IndexOf("-cp");
            string classpath = plan.Arguments[cp + 1];
            Assert(classpath.Contains("common.jar") && !classpath.Contains("renderer.jar") && classpath.EndsWith("projectzomboid.jar;."), "Wrong overlay precedence or client rendering loaded on server.");
            Assert(plan.Arguments[cp + 2] == "zombie.network.GameServer" && !plan.Arguments.Contains("-coop"), "Dedicated main replaced.");
            Assert(!plan.CommandLinePreview.Contains("test-secret") && plan.Arguments.Contains("-Dpzlauncher.runtimeSide=server"), "Secret leaked or runtime role lost.");
            Assert(ServerFiles.IniValues(File.ReadAllText(preset.Ini))["Mods"] == "\\PZLauncherRuntimeFixture", "Pack mod not enabled.");
            Assert(pack.JavaClasspath(Path.GetDirectoryName(ServerRuntimePack.Manifest(preset))!, "client").Count == 2, "Client side excludes shared or rendering patch.");
        });
        check("Pack : jeu différent, manifeste modifié et fichier installé altéré refusés", () =>
        {
            var (manifest, pack) = Fixture(); var preset = Import(manifest);
            string installed = WorldFiles.Within(preset.CachePath, "mods/Fixture/42/media/lua/server/fixture.lua");
            File.AppendAllText(installed, "changed"); Reject(() => ServerFiles.Plan(installation, preset));
            File.WriteAllText(installed, "-- fixture\n"); File.AppendAllText(ServerRuntimePack.Manifest(preset), " "); Reject(() => ServerFiles.Plan(installation, preset));
            pack.GameJarSha256 = new string('0', 64); Write(manifest, pack); Reject(() => Import(manifest));
        });
        check("Pack : chevauchement de classes, chemins sortants et fichiers non déclarés refusés", () =>
        {
            var (manifest, pack) = Fixture(); string folder = Path.GetDirectoryName(manifest)!;
            var renderer = pack.Files.Single(f => f.Kind == "java" && f.Side == "client"); renderer.Side = "both";
            string path = WorldFiles.Within(folder, renderer.Path); File.Delete(path);
            using (var jar = ZipFile.Open(path, ZipArchiveMode.Create)) jar.CreateEntry("fixture/Common.class");
            renderer.Sha256 = WorldFiles.Hash(path); Write(manifest, pack); Reject(() => Import(manifest));
            renderer.Side = "client"; Write(manifest, pack);
            File.WriteAllText(Path.Combine(folder, "extra.lua"), "-- extra"); Reject(() => Import(manifest)); File.Delete(Path.Combine(folder, "extra.lua"));
            pack.Files[0].Path = "../escape.jar"; Write(manifest, pack); Reject(() => Import(manifest));
        });
        check("Pack : désactivation, ajout Lua et copie concurrente du mod détectés", () =>
        {
            var (manifest, _) = Fixture(); var preset = Import(manifest);
            string ini = File.ReadAllText(preset.Ini); File.WriteAllText(preset.Ini, ServerFiles.UpdateValue(ini, "Mods", "")); Reject(() => ServerFiles.Plan(installation, preset));
            File.WriteAllText(preset.Ini, ini);
            string extra = WorldFiles.Within(preset.CachePath, "mods/Fixture/42/media/lua/server/extra.lua"); File.WriteAllText(extra, "-- extra"); Reject(() => ServerFiles.Plan(installation, preset)); File.Delete(extra);
            string duplicate = WorldFiles.Within(preset.CachePath, "mods/Duplicate/42/mod.info"); Directory.CreateDirectory(Path.GetDirectoryName(duplicate)!); File.WriteAllText(duplicate, "id=PZLauncherRuntimeFixture\n");
            Reject(() => ServerFiles.Plan(installation, preset));
        });
        check("Pack : backup et restauration conservent runtime, empreinte et mod sans source externe", () =>
        {
            var (manifest, _) = Fixture(); var preset = Import(manifest);
            string archive = ServerFiles.Backup(preset, Path.Combine(root, "pack-backups"));
            var restored = ServerFiles.Restore(archive, Path.Combine(root, "pack-restores"));
            Assert(restored.RuntimePackSha256 == preset.RuntimePackSha256 && restored.CachePath != preset.CachePath, "Runtime pin or isolation lost.");
            var plan = ServerFiles.Plan(installation, restored);
            Assert(plan.Arguments.Any(a => a.Contains(restored.CachePath) && a.Contains("common.jar")), "Restored plan still points at old cache.");
        });
    }
    private static void Write(string manifest, ServerRuntimePack pack) => File.WriteAllText(manifest, JsonSerializer.Serialize(pack, ServerRuntimePack.Json));
    internal static int Verify(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory); var results = new List<object>(); int failed = 0;
        Run(outputDirectory, (name, action) => { try { action(); results.Add(new { name, passed = true }); } catch (Exception e) { failed++; results.Add(new { name, passed = false, error = e.ToString() }); } });
        File.WriteAllText(Path.Combine(outputDirectory, "verification.json"), JsonSerializer.Serialize(new { failed, results }, ServerRuntimePack.Json)); return failed == 0 ? 0 : 1;
    }
    internal static int Smoke(string manifest, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory); string installation = InstallationLocator.Find("");
        var lines = new ConcurrentQueue<string>(); Process? process = null; ServerPreset? preset = null; bool processStarted = false;
        try
        {
            preset = ServerRuntimePack.Create(manifest, installation, "elz-smoke", Path.Combine(outputDirectory, "server-caches"), false);
            preset.XmsMb = 512; preset.XmxMb = 2048;
            
            string observer = WorldFiles.Within(preset.CachePath, "mods/PZLauncherRuntimeSmoke/42");
            Directory.CreateDirectory(Path.Combine(observer, "media", "lua", "server"));
            File.WriteAllText(Path.Combine(observer, "mod.info"), "name=Dedicated smoke observer\nid=PZLauncherRuntimeSmoke\nrequire=EpicLootZ\nversionMin=42.20.0\n");
            File.WriteAllText(Path.Combine(observer, "media", "lua", "server", "ZZ_RuntimeSmoke.lua"), """
                Events.OnServerStarted.Add(function()
                    assert(isServer() and not isClient(), "Smoke observer requires a dedicated server")
                    local elz = EpicLootZ
                    assert(elz and elz.ServerHealth and type(elz.ServerHealth.onHitZombie) == "function", "ELZ combat service missing")
                    assert(elz.EconomyService and type(elz.EconomyService.onClientCommand) == "function", "ELZ economy service missing")
                    assert(ModData.get(elz.GLOBAL_STATE_KEY) ~= nil, "ELZ persistent state was not initialized")
                    print("[PZLauncherPackSmoke] EpicLootZ server services and persistent state ready")
                end)
                """);
            using var port1 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); using var port2 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            string ini = File.ReadAllText(preset.Ini);
            ini = ServerFiles.UpdateValue(ini, "Mods", ServerFiles.IniValues(ini)["Mods"] + ";\\PZLauncherRuntimeSmoke");
            foreach (var (key, value) in new[] { ("DefaultPort", ((IPEndPoint)port1.Client.LocalEndPoint!).Port.ToString()), ("UDPPort", ((IPEndPoint)port2.Client.LocalEndPoint!).Port.ToString()), ("Public", "false"), ("Open", "false"), ("BackupsOnStart", "false") }) ini = ServerFiles.UpdateValue(ini, key, value);
            File.WriteAllText(preset.Ini, ini);
            var plan = ServerFiles.Plan(installation, preset, Guid.NewGuid().ToString("N"));
            File.WriteAllText(Path.Combine(outputDirectory, "command.txt"), plan.CommandLinePreview + " -ip 127.0.0.1");
            var start = new ProcessStartInfo(plan.JavaExecutable) { WorkingDirectory = plan.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
            start.Environment.Remove("_JAVA_OPTIONS"); foreach (string arg in plan.Arguments) start.ArgumentList.Add(arg);
            start.ArgumentList.Add("-ip"); start.ArgumentList.Add("127.0.0.1");
            process = new() { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lines.Enqueue(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lines.Enqueue(e.Data); };
            port1.Dispose(); port2.Dispose(); process.Start(); processStarted = true; process.BeginOutputReadLine(); process.BeginErrorReadLine();
            var timer = Stopwatch.StartNew();
            while (!process.HasExited && timer.Elapsed < TimeSpan.FromMinutes(3) && !lines.Any(l => l.Contains("[PZLauncherPackSmoke] EpicLootZ server services and persistent state ready"))) Thread.Sleep(100);
            bool started = lines.Any(l => l.Contains("*** SERVER STARTED ****"));
            bool modLoaded = lines.Any(l => l.Contains("[PZLauncherPackSmoke] EpicLootZ server services and persistent state ready"));
            if (!process.HasExited) { process.StandardInput.WriteLine("quit"); process.StandardInput.Flush(); }
            if (!process.WaitForExit(60000)) throw new TimeoutException("Dedicated server did not stop after quit.");
            process.WaitForExit();
            if (!started || !modLoaded || process.ExitCode != 0) throw new InvalidOperationException("Dedicated startup, EpicLootZ bootstrap or clean quit failed.");
            File.WriteAllText(Path.Combine(outputDirectory, "smoke.json"), JsonSerializer.Serialize(new { passed = true, started, modLoaded, exitCode = process.ExitCode, cache = preset.CachePath, manifestHash = preset.RuntimePackSha256, scope = "Dedicated server startup and clean shutdown; no connected player or combat validation." }, ServerRuntimePack.Json));
            return 0;
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(outputDirectory, "smoke.json"), JsonSerializer.Serialize(new { passed = false, error = e.ToString(), cache = preset?.CachePath }, ServerRuntimePack.Json)); return 1;
        }
        finally
        {
            
            if (processStarted && process is { HasExited: false }) { process.Kill(true); process.WaitForExit(); }
            process?.Dispose(); File.WriteAllLines(Path.Combine(outputDirectory, "server-console.txt"), lines);
        }
    }
}
