using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PZLauncher.Services;

namespace PZLauncher.Desktop;


internal static class RuntimeControlVerification
{
    internal static int Run(string manifest, string output, string jdk)
    {
        Directory.CreateDirectory(output); string installation = InstallationLocator.Find(""); var results = new List<object>(); int failed = 0;
        foreach (string mode in new[] { "none", "pzlauncher", "pack" })
        {
            string folder = Path.Combine(output, mode); Directory.CreateDirectory(folder);
            var lines = new ConcurrentQueue<string>(); Process? process = null;
            try
            {
                var server = mode == "pack" ? ServerRuntimePack.Create(manifest, installation, "runtime-control", Path.Combine(folder, "servers"), false)
                    : new ServerPreset { Name = "java-control", CachePath = Path.Combine(folder, "server-cache"), Steam = false };
                if (mode != "pack") ServerFiles.Create(server);
                server.XmsMb = 512; server.XmxMb = 2048;
                string observer = WorldFiles.Within(server.CachePath, "mods/PZLauncherRuntimeSmoke/42");
                Directory.CreateDirectory(Path.Combine(observer, "media/lua/server"));
                File.WriteAllText(Path.Combine(observer, "mod.info"), "name=Generic runtime smoke observer\nid=PZLauncherRuntimeSmoke\n");
                File.WriteAllText(Path.Combine(observer, "media/lua/server/ZZ_RuntimeSmoke.lua"), "Events.OnServerStarted.Add(function() assert(isServer() and not isClient()); print('[PZLauncherServerJavaSmoke] dedicated-ready') end)\n");
                using var port1 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); using var port2 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                string ini = File.ReadAllText(server.Ini);
                foreach (var (key, value) in new[] { ("DefaultPort", ((IPEndPoint)port1.Client.LocalEndPoint!).Port.ToString()), ("UDPPort", ((IPEndPoint)port2.Client.LocalEndPoint!).Port.ToString()), ("Public", "false"), ("Open", "false"), ("BackupsOnStart", "false"), ("Mods", string.Join(';', server.EnabledMods().Append("PZLauncherRuntimeSmoke").Select(id => "\\" + id))) }) ini = ServerFiles.UpdateValue(ini, key, value);
                File.WriteAllText(server.Ini, ini);
                if (mode != "pack")
                {
                    var p = server.JavaProfile(); p.JavaLoader = mode; p.JavaLoaderEnabled = true;
                    string jar = Path.Combine(JavaMods.Folder(p), mode + "-smoke.jar"); Directory.CreateDirectory(Path.GetDirectoryName(jar)!);
                    if (mode == "none")
                    {
                        using var game = ZipFile.OpenRead(Path.Combine(installation, "projectzomboid.jar")); using var direct = ZipFile.Open(jar, ZipArchiveMode.Create);
                        using (var writer = new StreamWriter(direct.CreateEntry("META-INF/MANIFEST.MF").Open())) writer.Write("Manifest-Version: 1.0\nPZ-Side: server\n\n");
                        using var source = game.GetEntry("zombie/network/GameServer.class")!.Open(); using var target = direct.CreateEntry("zombie/network/GameServer.class").Open(); source.CopyTo(target);
                    }
                    else
                    {
                        var empty = JavaMods.Prepare(p, [], installation, "server");
                        string source = Path.Combine(folder, "ServerProbe.java"), classes = Path.Combine(folder, "classes"); Directory.CreateDirectory(classes);
                        File.WriteAllText(source, "package fixture; public final class ServerProbe implements community.pzloader.api.JavaMod { public void register(community.pzloader.api.PatchRegistry registry) { registry.transform(\"zombie.network.GameServer\", bytes -> bytes); } }");
                        var compile = new ProcessStartInfo(Path.Combine(jdk, "bin/javac.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                        foreach (string arg in new[] { "-cp", empty.AgentPath, "-d", classes, source }) compile.ArgumentList.Add(arg);
                        using var javac = Process.Start(compile)!; var compilerOutput = javac.StandardError.ReadToEndAsync(); var compilerStdout = javac.StandardOutput.ReadToEndAsync();
                        if (!javac.WaitForExit(30000)) { javac.Kill(true); throw new TimeoutException("javac"); }
                        if (javac.ExitCode != 0) throw new IOException(compilerOutput.GetAwaiter().GetResult());
                        using var zip = ZipFile.Open(jar, ZipArchiveMode.Create); zip.CreateEntryFromFile(Path.Combine(classes, "fixture/ServerProbe.class"), "fixture/ServerProbe.class");
                        using var descriptor = new StreamWriter(zip.CreateEntry(JavaMods.Descriptor).Open()); descriptor.Write("api=1\nid=generic_server_probe\nversion=1\nentrypoint=fixture.ServerProbe\ngameVersions=" + InstallationLocator.ReadGameVersion(installation) + "\nside=server\n");
                    }
                    var catalog = JavaMods.Scan(p, [], [], installation, "server");
                    JavaMods.SetSelected(p, [JavaMods.Inspect(jar, InstallationLocator.ReadGameVersion(installation)?.ToString() ?? "", side: "server").Id]);
                    var java = JavaMods.Prepare(p, catalog, installation, "server");
                    File.WriteAllText(Path.Combine(folder, "preflight.txt"), JavaMods.PreflightAsync(ServerFiles.Plan(installation, server, includeJava: false), java).GetAwaiter().GetResult());
                }
                var plan = ServerFiles.Plan(installation, server, Guid.NewGuid().ToString("N"));
                var args = plan.Arguments.ToList(); args.Insert(0, "-Xlog:class+load=info"); args.AddRange(["-ip", "127.0.0.1"]);
                plan = new LaunchPlan { JavaExecutable = plan.JavaExecutable, WorkingDirectory = plan.WorkingDirectory, CacheDirectory = plan.CacheDirectory, ConsoleLogPath = plan.ConsoleLogPath, Arguments = args };
                File.WriteAllText(Path.Combine(folder, "command.txt"), plan.CommandLinePreview);
                var start = new ProcessStartInfo(plan.JavaExecutable) { WorkingDirectory = installation, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
                start.Environment.Remove("_JAVA_OPTIONS"); foreach (string arg in plan.Arguments) start.ArgumentList.Add(arg);
                process = new() { StartInfo = start }; process.OutputDataReceived += (_, e) => { if (e.Data != null) lines.Enqueue(e.Data); }; process.ErrorDataReceived += (_, e) => { if (e.Data != null) lines.Enqueue(e.Data); };
                port1.Dispose(); port2.Dispose(); process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                var timer = Stopwatch.StartNew();
                while (!process.HasExited && timer.Elapsed < TimeSpan.FromMinutes(3) && !lines.Any(l => l.Contains("[PZLauncherServerJavaSmoke] dedicated-ready"))) Thread.Sleep(100);
                bool ready = lines.Any(l => l.Contains("[PZLauncherServerJavaSmoke] dedicated-ready"));
                bool javaLoaded = mode == "pack" || lines.Any(l => mode == "none" ? l.Contains("zombie.network.GameServer source:") && l.Contains("none-smoke.jar") : l.Contains("[PZLoader] Applied zombie/network/GameServer"));
                if (!ready || !javaLoaded || process.HasExited) throw new IOException("Server ready marker or expected Java source/transform missing.");
                if (mode == "pack") VerifyClient(server, installation, folder, process);
                process.StandardInput.WriteLine("quit"); process.StandardInput.Flush();
                if (!process.WaitForExit(60000)) throw new TimeoutException("Server did not quit cleanly.");
                process.WaitForExit(); if (process.ExitCode != 0) throw new IOException("Server exit " + process.ExitCode);
                results.Add(new { mode, passed = true, ready, javaLoaded, exitCode = process.ExitCode, cache = server.CachePath });
            }
            catch (Exception e) { failed++; results.Add(new { mode, passed = false, error = e.ToString() }); }
            finally
            {
                if (process != null) { try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } } catch (InvalidOperationException) { } process.Dispose(); }
                File.WriteAllLines(Path.Combine(folder, "server-console.txt"), lines);
                File.WriteAllText(Path.Combine(output, "smoke.json"), JsonSerializer.Serialize(new { failed, results, scope = "Real dedicated startup and Java loading. Client startup through launcher; no authenticated player or multiplayer gameplay validation." }, ServerRuntimePack.Json));
            }
        }
        return failed == 0 ? 0 : 1;
    }
    private static void VerifyClient(ServerPreset server, string installation, string folder, Process serverProcess)
    {
        var client = RuntimeClients.Create(server, installation, "Generic pack client", Path.Combine(folder, "clients"));
        client.AutomaticJvm = false; client.MemoryMb = 4096; client.InitialMemoryMb = 512; client.Collector = "ZGC"; client.Voice = false; client.Launch.NoSound = true; client.ExtraJvmArguments = ["-Xlog:class+load=info"];
        File.WriteAllText(Path.Combine(client.CachePath, "options.ini"), "version=7\nwidth=1280\nheight=720\nfullScreen=false\nborderlessWindow=false\n");
        string data = Path.Combine(folder, "launcher-data"), launchOutput = Path.Combine(folder, "client-launch"); Directory.CreateDirectory(data); Directory.CreateDirectory(launchOutput);
        var state = new LauncherState { Installation = installation, Language = "fr", Profiles = [client], Servers = [server], SelectedProfile = client.Id };
        LauncherStorage.WriteAtomic(Path.Combine(data, "launcher.json"), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }; start.Environment["PZLAUNCHER_DATA"] = data;
        start.ArgumentList.Add("--verify-launch=" + launchOutput); start.ArgumentList.Add("--verify-cache=" + client.CachePath);
        using var launcher = Process.Start(start)!; Process? game = null;
        try
        {
            var timer = Stopwatch.StartNew(); string launchJson = Path.Combine(launchOutput, "launch.json");
            while (!launcher.HasExited && timer.Elapsed < TimeSpan.FromSeconds(90) && !File.Exists(launchJson) && !File.Exists(Path.Combine(launchOutput, "error.txt"))) Thread.Sleep(200);
            if (!File.Exists(launchJson)) throw new IOException("Launcher did not start the client; see client-launch/error.txt.");
            using var json = JsonDocument.Parse(File.ReadAllText(launchJson)); game = Process.GetProcessById(json.RootElement.GetProperty("processId").GetInt32());
            if (!RuntimeClients.SameCache(json.RootElement.GetProperty("plan").GetProperty("CacheDirectory").GetString()!, client.CachePath) || json.RootElement.GetProperty("Id").GetString() != client.Id)
                throw new IOException("The launcher used a different client profile/cache.");
            timer.Restart();
            while (!game.HasExited && timer.Elapsed < TimeSpan.FromSeconds(65)) { game.Refresh(); if (game.MainWindowHandle != IntPtr.Zero && timer.Elapsed > TimeSpan.FromSeconds(45)) break; Thread.Sleep(200); }
            game.Refresh(); bool simultaneous = !serverProcess.HasExited && !game.HasExited && game.MainWindowHandle != IntPtr.Zero;
            File.WriteAllText(Path.Combine(launchOutput, "observed.json"), JsonSerializer.Serialize(new { simultaneous, serverPid = serverProcess.Id, clientPid = game.Id, launcherPid = launcher.Id, clientWindow = game.MainWindowHandle.ToInt64(), serverCache = server.CachePath, clientCache = client.CachePath, connection = client.Launch.ConnectAddress, authenticatedPlayer = false }, ServerRuntimePack.Json));
            if (!simultaneous) throw new IOException("Client window and live dedicated process were not observed together.");
        }
        finally
        {
            if (game != null) { if (!game.HasExited) { game.CloseMainWindow(); if (!game.WaitForExit(15000)) { game.Kill(true); game.WaitForExit(); } } game.Dispose(); }
            if (!launcher.HasExited) { launcher.CloseMainWindow(); if (!launcher.WaitForExit(10000)) { launcher.Kill(true); launcher.WaitForExit(); } }
        }
    }
}
