using System.Text.Json;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class JvmLaunchVerification
{
    internal static void Run(string root, Action<string, Action> check)
    {
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        check("Java direct : le vrai lancement client sans runtime pack refuse les classes concurrentes", () =>
        {
            string directory = Path.Combine(root, "direct-conflict"), install = Path.Combine(directory, "installation");
            Directory.CreateDirectory(Path.Combine(install, "jre64", "bin"));
            File.WriteAllText(Path.Combine(install, "jre64", "bin", "java.exe"), "fixture, never executed");
            ServerJavaVerification.Jar(Path.Combine(install, "projectzomboid.jar"), ("fixture/Game.class", "fixture"));
            var p = new PlayerProfile { CachePath = Path.Combine(directory, "cache"), AutomaticJvm = false,
                MemoryMb = 2048, InitialMemoryMb = 1024, JavaLoaderEnabled = true, JavaLoader = JavaMods.None };
            string a = Path.Combine(JavaMods.Folder(p), "first.jar"), b = Path.Combine(JavaMods.Folder(p), "second.jar");
            const string duplicate = "zombie/characters/IsoGameCharacter.class";
            ServerJavaVerification.Jar(a, (duplicate, "first")); ServerJavaVerification.Jar(b, (duplicate, "second"));
            var catalog = JavaMods.Scan(p, [], [], install);
            JavaMods.SetSelected(p, catalog.Select(m => m.Id));
            string output = Path.Combine(directory, "launch");
            using var window = new LauncherWindow(verification: true, verifyLaunchDirectory: output, expectedLaunchCache: p.CachePath);
            window.VerifyDirectPatchConflict(install, p);
            string error = Path.Combine(output, "error.txt");
            Assert(File.Exists(error) && File.ReadAllText(error).Contains(duplicate)
                && File.ReadAllText(error).Contains(nameof(RuntimeClients.CheckOverlayConflicts)), "Client did not reject the overlapping classes before Java start.");
            Assert(!File.Exists(Path.Combine(output, "launch.json")), "Conflicting client was started.");
            var server = new ServerPreset { CachePath = p.CachePath, Java = p, Steam = false };
            bool rejected = false; try { ServerFiles.Plan(install, server); }
            catch (InvalidDataException ex) when (ex.Message.Contains(duplicate)) { rejected = true; }
            Assert(rejected, "Server accepted the same conflict.");
            JavaMods.SetSelected(p, [catalog.Single(m => m.Path == a).Id]);
            var java = JavaMods.Prepare(p, catalog, install);
            RuntimeClients.CheckOverlayConflicts([], java);
            var plan = JavaMods.Attach(new GameLaunchService().CreateLaunchPlan(install, p.AsGameProfile(), p.AsJvmProfile()), java);
            Assert(plan.Arguments.Contains(a + ";.;projectzomboid.jar"), "Valid patch classpath or order changed.");
            Assert(ServerFiles.Plan(install, server).Arguments.Contains(a + ";.;projectzomboid.jar"), "Valid server patch classpath changed.");
        });
        check("JVM : lancement autonome sans BAT/JSON et JSON invalide sans effet", () =>
        {
            string install = Path.Combine(root, "independent-runtime"); Directory.CreateDirectory(Path.Combine(install, "jre64", "bin"));
            File.WriteAllText(Path.Combine(install, "jre64", "bin", "java.exe"), "fixture, never executed");
            File.WriteAllText(Path.Combine(install, "jre64", "release"), "JAVA_VERSION=\"25.0.1\"");
            File.WriteAllText(Path.Combine(install, "projectzomboid.jar"), "fixture");
            var profile = new PlayerProfile { CachePath = root, MemoryMb = 8192, InitialMemoryMb = 2048, Collector = "ZGC" };
            var service = new GameLaunchService(); var plan = service.CreateLaunchPlan(install, profile.AsGameProfile(), profile.AsJvmProfile());
            Assert(InstallationLocator.IsValid(install) && plan.Arguments.Contains(".;projectzomboid.jar") && plan.Arguments.Contains("--enable-native-access=ALL-UNNAMED"), "Missing standalone runtime arguments.");
            File.WriteAllText(Path.Combine(install, "ProjectZomboid64.json"), "invalid JSON");
            Assert(plan.Arguments.SequenceEqual(service.CreateLaunchPlan(install, profile.AsGameProfile(), profile.AsJvmProfile()).Arguments), "JSON affects launcher-owned runtime.");
            Assert(HardwareAdvisor.Detect(install).JavaVersion == "25.0.1", "Bad JSON blocks hardware inventory.");
        });
        check("JVM : profil BAT, propriétés conservées, priorité des classes et aucun agent", () =>
        {
            string install = InstallationLocator.Find("");
            var profile = new PlayerProfile { CachePath = root, JvmArgumentBase = "custom", Steam = false, Voice = false, Debug = true,
                MemoryMb = 8192, InitialMemoryMb = 8192, StackKb = 2048, Collector = "G1", PauseTargetMs = 20, StringDeduplication = true,
                ExtraJvmArguments = ["-XX:+UnlockExperimentalVMOptions", "-XX:G1NewSizePercent=30", "-Djava.library.path=./win64/;./", "-Dpz.solar.voidColor=#6A6968", "-Xverify:all"] };
            var restored = JsonSerializer.Deserialize<PlayerProfile>(JsonSerializer.Serialize(profile))!;
            var copied = new PlayerProfile(); copied.CopyLocalJvmFrom(profile); copied.ExtraJvmArguments.Add("-Dcopy=true");
            Assert(copied.JvmArgumentBase == "custom" && !profile.ExtraJvmArguments.Contains("-Dcopy=true"), "Local JVM profile copy shared mutable arguments.");
            var plan = new GameLaunchService().CreateLaunchPlan(install, restored.AsGameProfile(), restored.AsJvmProfile());
            var attached = JavaMods.Attach(plan, new("", "", []) { Loader = JavaMods.None, LooseClassesFirst = true, Classpath = ["patch-a.jar", "patch-b.jar"] });
            Assert(attached.Arguments.Contains(".;patch-a.jar;patch-b.jar;projectzomboid.jar"), "Loose classes or patch order lost.");
            Assert(!attached.Arguments.Any(a => a.StartsWith("-javaagent:")) && !attached.Arguments.Contains("-Djava.awt.headless=true"), "BAT custom runtime polluted.");
            Assert(profile.ExtraJvmArguments.All(attached.Arguments.Contains) && attached.Arguments.Contains("-Xss2048k") && attached.Arguments.Contains("-debug") && attached.Arguments.Contains("-novoip"), "BAT arguments lost.");
            string export = JsonSerializer.Serialize(ProfileSharing.Export(profile, "42.20", []));
            Assert(!export.Contains("pz.solar") && !export.Contains("JvmArgumentBase"), "Local custom arguments leaked into shared profile.");
            foreach (string bad in new[] { "-cp", "-Xmx16384m", "-javaagent:evil.jar", "-XX:+UseZGC", "-XX:-UseG1GC", "-XX:MaxHeapSize=123", "-Dzomboid.steam=1", "-Dvalue=a\nb" })
            {
                bool rejected = false; try { JvmArguments.Validate("launcher", [bad]); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "Managed/runtime argument accepted: " + bad);
            }
        });
        check("JVM : adaptation indépendante du JSON, mémoire disponible et ancien Java", () =>
        {
            var pc = new HardwareSnapshot(65536, 32768, 32, "CPU", "25.0.1", "G1");
            var normal = HardwareAdvisor.Suggest(pc);
            Assert(normal.Collector == "ZGC" && normal.XmxMb == 8192 && normal.PauseTargetMs == 200, "PC adaptation depends on JSON.");
            Assert(HardwareAdvisor.Suggest(pc with { AvailableMb = 4096 }).XmxMb < normal.XmxMb, "Available memory ignored.");
            Assert(HardwareAdvisor.Suggest(pc with { JavaVersion = "17.0.1" }).Collector == "G1", "Legacy Java gets modern automatic collector.");
            var fresh = new PlayerProfile(); HardwareAdvisor.FillUnset(fresh, normal); Assert(fresh.AutomaticJvm, "Fresh profile not adaptive.");
            var manual = new PlayerProfile { MemoryMb = 6144, InitialMemoryMb = 1024, Collector = "G1" };
            HardwareAdvisor.FillUnset(manual, normal); Assert(!manual.AutomaticJvm && manual.MemoryMb == 6144, "Existing manual choices replaced.");
            using var window = new LauncherWindow(verification: true); window.VerifyJvmArgumentsDialog(root);
        });
    }
}

internal sealed partial class LauncherWindow
{
    internal void VerifyDirectPatchConflict(string installation, PlayerProfile candidate)
    {
        state.Installation = installation; state.Profiles = [candidate]; state.Servers = [];
        profile = candidate; selection = new(candidate.CachePath); mods = [];
        LaunchGame();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (preparingLaunch && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        if (preparingLaunch) throw new TimeoutException("Client conflict verification did not complete.");
    }
    internal void VerifyJvmArgumentsDialog(string root)
    {
        using var dialog = BuildJvmArgumentsDialog(out var basis, out var extra, out var loose);
        dialog.Show(); Application.DoEvents();
        extra.Text = "-Dpz.solar.enabled=true\r\n-Xverify:all"; basis.SelectedIndex = 2; loose.Checked = true;
        if (JvmArguments.Parse(extra.Text).Count != 2 || extra.ClientSize.Height < 130 || extra.ClientSize.Width < 600)
            throw new InvalidOperationException("JVM argument editor cannot display a BAT profile.");
        dialog.Refresh(); basis.Refresh();
        using var bitmap = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(root, "jvm-arguments.png")); dialog.Close();
    }
}
