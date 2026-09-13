using System.Text.Json;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class InstallationVerification
{
    internal static void Run(string root, Action<string, Action> check)
    {
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        string data = Path.Combine(root, "installations");
        string steam = Path.Combine(data, "Steam"), hdd = Path.Combine(data, "HDD", "SteamLibrary"), ssd = Path.Combine(data, "SSD", "SteamLibrary");
        string first = Path.Combine(steam, "steamapps", "common", "ProjectZomboid");
        string second = Path.Combine(hdd, "steamapps", "common", "PZ custom folder");
        string third = Path.Combine(ssd, "steamapps", "common", "ProjectZomboid");
        string gog = Path.Combine(data, "GOG", "Project Zomboid"), manual = Path.Combine(data, "Custom été", "PZ");
        void Game(string path)
        {
            Directory.CreateDirectory(Path.Combine(path, "jre64", "bin"));
            File.WriteAllText(Path.Combine(path, "jre64", "bin", "java.exe"), "fixture, never executed");
            using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(path, "projectzomboid.jar"), System.IO.Compression.ZipArchiveMode.Create)) { }
            File.WriteAllText(Path.Combine(path, "ProjectZomboid64.json"), """{"vmArgs":["-Dzomboid.steam=1"],"classpath":["projectzomboid.jar"],"mainClass":"zombie/gameStates/MainScreenState"}""");
        }
        foreach (string path in new[] { first, second, third, gog, manual }) Game(path);
        File.WriteAllText(Path.Combine(gog, "goggame-1234567890.info"), "{}");
        string Q(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        File.WriteAllText(vdf, "\"libraryfolders\" { \"0\" { \"path\" " + Q(steam) + " } \"1\" { \"path\" " + Q(hdd) + " \"apps\" { \"108600\" \"100\" } } \"2\" " + Q(ssd) + " // legacy path\n }");
        File.WriteAllText(Path.Combine(hdd, "steamapps", "appmanifest_108600.acf"), "\"AppState\" { \"appid\" \"108600\" \"installdir\" \"PZ custom folder\" }");
        check("Installation : plusieurs bibliothèques Steam, formats VDF moderne/ancien et dossier du manifeste", () =>
        {
            var libraries = InstallationLocator.SteamLibraries([steam, steam.ToUpperInvariant()]);
            Assert(libraries.Count == 3 && libraries.Contains(hdd) && libraries.Contains(ssd), "A Steam disk was lost or duplicated.");
            var result = InstallationLocator.Discover(steamRoots: [steam], gogPaths: []);
            Assert(result.Count == 3 && result.All(i => i.Source == "steam") && result.Any(i => i.Path == second), "Steam manifest folder was ignored.");
        });
        check("Installation : GOG et dossier personnalisé validés, dédupliqués et conservés au redémarrage", () =>
        {
            var result = InstallationLocator.Discover(manual, [manual.ToUpperInvariant()], [steam], [gog]);
            Assert(result.Count == 5 && result[0].Path == manual && result[0].Source == "manual" && result.Single(i => i.Path == gog).Source == "gog", "Installation sources or preferred choice were lost.");
            var state = new LauncherState { Installation = manual };
            InstallationLocator.Remember(state, "\"" + Path.Combine(gog, "ProjectZomboid64.json") + "\"");
            Assert(state.Installation == gog && state.InstallationPaths.Contains(manual), "Manual path or old installation was lost.");
            var restored = JsonSerializer.Deserialize<LauncherState>(JsonSerializer.Serialize(state))!;
            Assert(InstallationLocator.Find(restored.Installation) == gog && restored.InstallationPaths.Count == 2, "Selected installation did not persist.");
            bool rejected = false;
            try { InstallationLocator.Remember(state, Path.Combine(data, "incomplete")); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected && state.Installation == gog, "Invalid folder replaced a working installation.");
            string missing = Path.Combine(data, "Unplugged drive", "PZ");
            Assert(InstallationLocator.Find(missing) == missing, "An unavailable custom path was silently replaced.");
        });
        check("Installation : catalogue résilient aux manifestes incomplets et chemins sortant de la bibliothèque", () =>
        {
            string alternate = Path.Combine(steam, "config"); Directory.CreateDirectory(alternate);
            File.WriteAllText(Path.Combine(alternate, "libraryfolders.vdf"), File.ReadAllText(vdf));
            File.WriteAllText(vdf, "\"libraryfolders\" { \"broken\"");
            Assert(InstallationLocator.Discover(steamRoots: [steam], gogPaths: []).Count == 3, "A bad VDF prevented reading another library list.");
            string outside = Path.Combine(hdd, "outside"); Game(outside);
            File.WriteAllText(Path.Combine(hdd, "steamapps", "appmanifest_108600.acf"), "\"AppState\" { \"appid\" \"108600\" \"installdir\" \"../../outside\" }");
            Assert(!InstallationLocator.Discover(steamRoots: [steam], gogPaths: []).Any(i => i.Path == outside), "An escaped install folder was accepted as a Steam installation.");
        });
        check("Installation : lancement GOG sans Steam, préférences conservées et Java du dossier choisi", () =>
        {
            var profile = new PlayerProfile { Steam = true, CachePath = Path.Combine(data, "profile") };
            var service = new GameLaunchService();
            var plan = service.CreateLaunchPlan(gog, profile.AsGameProfile(), profile.AsJvmProfile());
            Assert(plan.Arguments.Contains("-Dzomboid.steam=0") && !plan.Arguments.Contains("-Dzomboid.steam=1") && profile.Steam, "GOG Steam mode was wrong or changed the saved preference.");
            Assert(plan.JavaExecutable == Path.Combine(gog, "jre64", "bin", "java.exe") && plan.WorkingDirectory == gog, "Launch used another installation's Java or classpath base.");
            Assert(service.CreateLaunchPlan(first, profile.AsGameProfile(), profile.AsJvmProfile()).Arguments.Contains("-Dzomboid.steam=1"), "Returning to Steam lost its preference.");
            var hosted = ServerFiles.Plan(gog, new ServerPreset { Name = "gog", CachePath = profile.CachePath, Steam = true });
            Assert(hosted.Arguments.Contains("-nosteam") && hosted.Arguments.Contains("-Dzomboid.steam=0"), "GOG hosting attempted Steam initialization.");
        });
        check("Installation : classes non empaquetées et classpath officiel conservé", () =>
        {
            string loose = Path.Combine(data, "Loose game");
            Directory.CreateDirectory(Path.Combine(loose, "jre64", "bin"));
            File.WriteAllText(Path.Combine(loose, "jre64", "bin", "java.exe"), "fixture, never executed");
            string classes = Path.Combine(loose, "java");
            Directory.CreateDirectory(Path.Combine(classes, "zombie", "gameStates"));
            Directory.CreateDirectory(Path.Combine(classes, "zombie", "core"));
            using (var archive = System.IO.Compression.ZipFile.OpenRead(Path.Combine(InstallationLocator.Find(""), "projectzomboid.jar")))
                foreach (string entryName in new[] { "zombie/gameStates/MainScreenState.class", "zombie/core/Core.class" })
                {
                    using var input = archive.GetEntry(entryName)!.Open();
                    using var output = File.Create(Path.Combine(classes, entryName)); input.CopyTo(output);
                }
            File.WriteAllText(Path.Combine(loose, "ProjectZomboid64.json"), """{"vmArgs":[],"classpath":["java/","java/dependency.jar"],"mainClass":"zombie/gameStates/MainScreenState"}""");
            Assert(InstallationLocator.IsValid(loose) && InstallationLocator.ReadGameVersion(loose)?.Major == 42, "Loose game code was not recognized/read.");
            var plan = new GameLaunchService().CreateLaunchPlan(loose, new() { CacheDir = data }, new());
            Assert(plan.Arguments.Contains("java/;java/dependency.jar"), "An installation's classpath was replaced by a hardcoded JAR.");
        });
        check("Installation : contrôles WinForms, liste multiple et saisie indépendante du chemin actif", () =>
        {
            using var window = new LauncherWindow(verification: true);
            window.VerifyInstallationControls(manual, gog, first);
        });
    }
}

internal sealed partial class LauncherWindow
{
    internal void VerifyInstallationControls(string manual, string gog, string steam)
    {
        Enabled = true; 
        profile = new() { CachePath = Path.Combine(manual, "profile"), Steam = true };
        state.Profiles = [profile]; selection = new(profile.CachePath);
        state.Installation = manual;
        installations = [new(manual, "manual", null), new(gog, "gog", new(42, 20)), new(steam, "steam", new(42, 20))];
        ShowPage("Réglages");
        var inputs = Descendants(pageHost).OfType<TextBox>();
        var active = inputs.Single(c => c.Name == "ActiveInstallation");
        var input = inputs.Single(c => c.Name == "InstallationPath");
        var list = Descendants(pageHost).OfType<ComboBox>().Single(c => c.Name == "DetectedInstallations");
        if (active.Text != manual || !active.ReadOnly || list.Items.Count != 3) throw new InvalidOperationException("Installation controls do not show the real selection.");
        input.Text = gog;
        if (state.Installation != manual) throw new InvalidOperationException("Editing an unapplied path switched installations.");
        ShowPage("Jouer"); ShowPage("Réglages");
        if (Descendants(pageHost).OfType<TextBox>().Single(c => c.Name == "InstallationPath").Text != gog)
            throw new InvalidOperationException("Navigation discarded the typed installation path.");
        var apply = ApplyInstallationAsync(gog);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!apply.IsCompleted && watch.Elapsed.TotalSeconds < 15) { Application.DoEvents(); Thread.Sleep(10); }
        if (!apply.IsCompleted) throw new TimeoutException("Changing installation did not finish.");
        apply.GetAwaiter().GetResult();
        if (state.Installation != gog || installationBusy || !pageHost.Enabled || !profile.Steam || profile.CachePath != Path.Combine(manual, "profile") ||
            Descendants(pageHost).OfType<TextBox>().Single(c => c.Name == "ActiveInstallation").Text != gog)
            throw new InvalidOperationException("Applying an installation lost its path, profile, preferences or enabled controls.");
    }
}
