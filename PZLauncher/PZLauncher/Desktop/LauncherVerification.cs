using System.Text.Json;
using System.Text.RegularExpressions;
using PZLauncher.Profiles;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class LauncherVerification
{
    internal static int Run()
    {
        var results = new List<object>(); int failures = 0;
        string root = Path.Combine(Path.GetTempPath(), "PZLauncher-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Check(string name, Action test)
        {
            try { test(); results.Add(new { name, passed = true }); }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); }
        }
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        Check("Mods : fermeture transitive, ordre stable et contraintes avant/après", () =>
        {
            InstalledMod[] catalog =
            [
                new() { Id = "A", Name = "A", Available = true, Requires = ["B"] },
                new() { Id = "B", Name = "B", Available = true, Requires = ["C"] },
                new() { Id = "C", Name = "C", Available = true },
                new() { Id = "D", Name = "D", Available = true, LoadBefore = ["A"], LoadAfter = ["C", "not-selected"] }
            ];
            var result = ModResolver.Resolve(catalog, ["A", "D"]);
            Assert(result.Success && result.Ordered.SequenceEqual(new[] { "C", "D", "B", "A" }), "Tri topologique instable.");
            Assert(result.Added.Order().SequenceEqual(new[] { "B", "C" }), "Dépendances non activées.");
            Assert(ModResolver.Resolve(catalog, result.Ordered).Ordered.SequenceEqual(result.Ordered), "Le tri n'est pas idempotent.");
            var fields = new Dictionary<string, string> { ["loadModAfter"] = @"\A, B", ["loadModBefore"] = "C", ["incompatible"] = "D" };
            Assert(ModCatalog.MetadataList(fields, "loadModAfter").SequenceEqual(new[] { "A", "B" }), "Métadonnées B42 mal lues.");
        });
        Check("Mods : signaler toutes les absences, cycles et incompatibilités", () =>
        {
            var missing = ModResolver.Resolve([new() { Id = "A", Name = "A", Available = true, Requires = ["X", "Y"] }], ["A"]);
            Assert(!missing.Success && missing.Issues.Count == 2, "Dépendances absentes non détaillées.");
            var cyclic = ModResolver.Resolve([new() { Id = "A", Available = true, Requires = ["B"] }, new() { Id = "B", Available = true, Requires = ["A"] }], ["A", "B"]);
            Assert(!cyclic.Success, "Cycle existant ignoré.");
            var conflict = ModResolver.Resolve([new() { Id = "A", Available = true, Incompatible = ["B"] }, new() { Id = "B", Available = true }], ["B", "A"]);
            Assert(!conflict.Success, "Incompatibilité à sens unique ignorée.");
        });
        Check("Configuration : aller-retour portable et profil indépendant", () =>
        {
            string cache = Path.Combine(root, "share-source"); Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "options.ini"), "soundVolume=7\nfutureFlag=kept\npassword=private\n");
            var source = new PlayerProfile { Name = "My shared setup", CachePath = cache, MemoryMb = 6144, InitialMemoryMb = 1024, Debug = true };
            source.Launch.ConnectAddress = "private-server"; source.Launch.ConnectPassword = "private-password"; source.Launch.DebugConfig = "C:/private/config.txt";
            var config = ProfileSharing.Export(source, "42.20", ["A", "B"]);
            string json = JsonSerializer.Serialize(config, ProfileSharing.JsonOptions);
            Assert(!json.Contains("private") && !json.Contains(cache) && !json.Contains(source.Id), "Données locales exportées.");
            string file = Path.Combine(root, "profile.pzconfig.json"); File.WriteAllText(file, json);
            var imported = ProfileSharing.Import(ProfileSharing.Read(file), Path.Combine(root, "imported-profiles"));
            Assert(imported.Id != source.Id && imported.CachePath != source.CachePath && imported.InitialMemoryMb == 1024, "Profil partagé non isolé.");
            Assert(new ModSelection(imported.CachePath).Ids.SequenceEqual(new[] { "A", "B" }), "Mods non importés.");
            Assert(new OptionDocument(Path.Combine(imported.CachePath, "options.ini")).Values["futureFlag"] == "kept", "Options inconnues perdues.");
            config.SchemaVersion = 999;
            bool rejected = false; try { ProfileSharing.Validate(config); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Version de format inconnue acceptée.");
        });
        Check("Matériel : proposition bornée et réserve mémoire", () =>
        {
            var large = HardwareAdvisor.Suggest(new(65536, 32768, 32, "CPU", "25", "ZGC"));
            Assert(large.XmxMb == 8192 && large.XmsMb == 2048, "Proposition grande machine incohérente.");
            var constrained = HardwareAdvisor.Suggest(new(8192, 4096, 4, "CPU", "25", "G1"));
            Assert(constrained.XmxMb <= 2048 && constrained.XmsMb <= constrained.XmxMb, "Pas de réserve pour le système.");
            var live = HardwareAdvisor.Detect(InstallationLocator.Find(""));
            Assert(live.TotalMb > 0 && live.Threads > 0 && live.JavaVersion.Length > 0, "Détection réelle incomplète.");
        });
        Check("Lancement : toutes les familles d'options et mot de passe masqué", () =>
        {
            var launch = new LaunchSettings { SafeMode = true, NoSound = true, AiTest = true, AntiCheats = true, ImGuiViewports = true,
                DebugTranslation = true, DebugLog = "+General,-Sound", DebugConfig = "C:/debug config.txt", ConnectAddress = "127.0.0.1", ConnectPassword = "private", ModFolders = "mods,steam" };
            launch.Validate(); var args = launch.Arguments().ToList();
            Assert(args.Contains("-imguidebugviewports") && args.Contains("-safemode") && args.Contains("-anti-cheats") &&
                args.Contains("-debugtranslation") && args.Contains("-nosound") && args.Contains("-aitest") &&
                args.Contains("-debugcfg=C:/debug config.txt") && args.Contains("-modfolders"), "Option PZ omise.");
            var plan = new LaunchPlan { Arguments = args };
            Assert(!plan.CommandLinePreview.Contains("private") && !JsonSerializer.Serialize(launch).Contains("private"), "Mot de passe exposé.");
            Assert(new PlayerProfile { Launch = launch }.AsJvmProfile().DebugEnabled, "ImGui doit impliquer Debug.");
        });
        Check("Langues : couverture complète et paramètres de format cohérents", () =>
        {
            var english = L.Catalog("en");
            string Parameters(string text) => string.Join(",", Regex.Matches(text, @"\{(\d+)(?:[^{}]*)\}")
                .Select(m => m.Groups[1].Value).Distinct().Order());
            foreach (var language in L.BuiltInLanguages)
            {
                var catalog = L.Catalog(language.Code);
                Assert(catalog.Keys.Order().SequenceEqual(english.Keys.Order()), "Catalogue incomplet : " + language.Code);
                SetLanguage(language.Code);
                foreach (var entry in english)
                {
                    Assert(!string.IsNullOrWhiteSpace(catalog[entry.Key]), "Traduction vide : " + entry.Key);
                    Assert(Parameters(entry.Value) == Parameters(catalog[entry.Key]), "Paramètres différents : " + entry.Key);
                    _ = T(entry.Key, Enumerable.Repeat<object>(1234, 16).ToArray());
                }
            }
            SetLanguage("unsupported");
            Assert(L.Code == "en" && T("nav.settings") == "Settings", "Le repli anglais a échoué.");
        });
        Check("Langues : anglais par défaut et préférence persistante sans modifier le jeu", () =>
        {
            var legacy = JsonSerializer.Deserialize<LauncherState>("""{"Installation":"test","Profiles":[{"Name":"Mon profil","Collector":"G1","Steam":false}]}""")!;
            Assert(legacy.Language == "en", "Un ancien fichier sans langue doit ouvrir l'interface en anglais.");
            string before = JsonSerializer.Serialize(legacy.Profiles);
            foreach (var language in L.Languages)
            {
                legacy.Language = language.Code;
                string path = Path.Combine(root, "launcher-language.json");
                LauncherStorage.WriteAtomic(path, JsonSerializer.Serialize(legacy));
                var loaded = JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(path))!;
                Assert(loaded.Language == language.Code && JsonSerializer.Serialize(loaded.Profiles) == before, "Une préférence a modifié le profil.");
            }
        });
        LanguagePackVerification.Run(root, Check);
        Check("Langues : allers-retours différés de toutes les langues, contrôle natif conservé", () =>
        {
            using var window = new LauncherWindow(verification: true); window.VerifyLanguageNotifications(root);
            SetLanguage("en");
        });
        Check("Langues : changements immédiats, filtres de mods et choix JVM stables", () =>
        {
            using var window = new LauncherWindow();
            window.VerifyLanguageBehavior(root);
            SetLanguage("en");
        });
        Check("Réglages : préserver les options inconnues, commentaires et sauvegarde", () =>
        {
            string path = Path.Combine(root, "options.ini");
            string original = "# user comment\r\nversion=8\r\nsoundVolume=5\r\nfutureFlag=keep=me\r\n";
            File.WriteAllText(path, original);
            new OptionDocument(path).SaveChanges(new Dictionary<string, string> { ["soundVolume"] = "2" });
            string changed = File.ReadAllText(path);
            Assert(changed.Contains("futureFlag=keep=me") && changed.Contains("# user comment") && changed.Contains("soundVolume=2"), "Les données originales ont changé.");
            Assert(File.ReadAllText(path + ".pzlauncher.bak") == original, "Copie de secours incorrecte.");
        });
        Check("Réglages : refuser un conflit avec une écriture du jeu", () =>
        {
            string path = Path.Combine(root, "conflict.ini"); File.WriteAllText(path, "soundVolume=5\n");
            var document = new OptionDocument(path); File.WriteAllText(path, "soundVolume=7\n");
            bool caught = false;
            try { document.SaveChanges(new Dictionary<string, string> { ["soundVolume"] = "2" }); } catch (IOException) { caught = true; }
            Assert(caught && File.ReadAllText(path).Contains("soundVolume=7"), "Le conflit n'a pas protégé la modification externe.");
        });
        Check("Mods : conserver l’ordre et les cartes lors de l’enregistrement", () =>
        {
            string cache = Path.Combine(root, "profile"); Directory.CreateDirectory(Path.Combine(cache, "mods"));
            string path = Path.Combine(cache, "mods", "default.txt");
            File.WriteAllText(path, "VERSION = 1,\nmods\n{\n mod = A,\n mod = B,\n}\nmaps\n{\n map = Louisville,\n}\n");
            var selected = new ModSelection(cache); selected.Ids.Reverse(); selected.Ids.Add("C"); selected.Save();
            Assert(new ModSelection(cache).Ids.SequenceEqual(new[] { "B", "A", "C" }), "L'ordre des mods n'est pas conservé.");
            Assert(File.ReadAllText(path).Contains("map = Louisville,"), "Les cartes ont été modifiées.");
        });
        Check("RSS : XML, date, illustration et provenance officielle", () =>
        {
            var items = NewsService.ParseFeed("<rss xmlns:content='http://purl.org/rss/1.0/modules/content/'><channel><item><title>Test &amp; update</title><link>https://projectzomboid.com/blog/news/test/</link><pubDate>Wed, 29 Jul 2026 11:22:51 +0000</pubDate><description>Résumé</description><content:encoded><![CDATA[<img src='https://projectzomboid.com/blog/image.jpg'>]]></content:encoded></item><item><title>Outside</title><link>https://example.com/</link></item></channel></rss>");
            Assert(items.Count == 1 && items[0].Title == "Test & update" && items[0].Published.Year == 2026 && items[0].ImageUrl.EndsWith("image.jpg"), "Flux incorrect.");
        });
        Check("Commande : configuration officielle, Steam 0/1 et chemins avec espaces", () =>
        {
            string install = Path.Combine(root, "PZ avec espaces"); Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "ProjectZomboid64.json"), """
                {"mainClass":"zombie/gameStates/MainScreenState","classpath":[".","projectzomboid.jar"],
                "vmArgs":["--enable-native-access=ALL-UNNAMED","--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED","-Xmx3072m","-Dzomboid.steam=1"],
                "windows":{"6.1":{"vmArgs":["-XX:+UseG1GC"]}}}
                """);
            var service = new GameLaunchService();
            foreach (bool steam in new[] { true, false })
            {
                var plan = service.CreateLaunchPlan(install, new GameProfile { CacheDir = Path.Combine(root, "profil été") },
                    new JvmProfile { SteamEnabled = steam, UseZgc = true, XmxMb = 4096 });
                Assert(plan.Arguments.Contains("--enable-native-access=ALL-UNNAMED") && plan.Arguments.Any(a => a.StartsWith("--add-exports=")), "Paramètres natifs perdus.");
                Assert(plan.Arguments.Count(a => a.StartsWith("-Dzomboid.steam=")) == 1 && plan.Arguments.Contains("-Dzomboid.steam=" + (steam ? "1" : "0")), "Choix Steam incohérent.");
                Assert(plan.Arguments.Contains("-Xmx4096m") && !plan.Arguments.Contains("-Xmx3072m") && !plan.Arguments.Contains("-XX:+UseG1GC"), "Options JVM contradictoires.");
                Assert(plan.Arguments.Contains("-cachedir=" + Path.Combine(root, "profil été")), "Chemin de profil découpé.");
            }
        });
        Check("Installation locale : préparation du lancement et lecture de version sans exécuter PZ", () =>
        {
            string install = InstallationLocator.Find("");
            Assert(InstallationLocator.IsValid(install), "Installation locale non détectée.");
            Version? version = InstallationLocator.ReadGameVersion(install);
            Assert(version?.Major == 42, "La version B42 n'a pas pu être lue dans le JAR.");
            var plan = new GameLaunchService().CreateLaunchPlan(install, new GameProfile { CacheDir = root }, new JvmProfile { SteamEnabled = true });
            Assert(File.Exists(plan.JavaExecutable) && plan.Arguments.Contains("zombie.gameStates.MainScreenState"), "Plan non utilisable.");
            results.Add(new { installation = install, version = version!.ToString(), arguments = plan.Arguments });
        });
        Check("JVM : Xms respecte aussi le Xmx officiel et les options G1 sont conditionnelles", () =>
        {
            string install = InstallationLocator.Find("");
            var service = new GameLaunchService();
            bool rejected = false;
            try { service.CreateLaunchPlan(install, new GameProfile { CacheDir = root }, new JvmProfile { XmsMb = 65536 }); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Xms dépasse le Xmx officiel sans être refusé.");
            var zgc = service.CreateLaunchPlan(install, new GameProfile { CacheDir = root }, new JvmProfile { UseZgc = true, PauseTargetMs = 200, StringDeduplication = true });
            Assert(!zgc.Arguments.Any(a => a.Contains("MaxGCPauseMillis") || a.Contains("UseStringDeduplication")), "Option du preset G1 appliquée à ZGC.");
        });
        Check("Java : descripteur, anciens JAR, dépendances et refus de version", () =>
        {
            string path = Path.Combine(root, "example.jar");
            using (var jar = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(jar.CreateEntry(JavaMods.Descriptor).Open()))
                writer.Write("id=example\nname=Example\nversion=1.0\napi=1\nentrypoint=sample.Main\nside=client\ngameVersions=42.20\nrequire=dependency\n");
            var entry = JavaMods.Inspect(path, "42.20");
            Assert(entry.Available && entry.Metadata.Requires.SequenceEqual(new[] { "dependency" }), "Descripteur non reconnu.");
            Assert(!JavaMods.Inspect(path, "42.99").Available, "Version non déclarée acceptée.");
            Assert(!ModResolver.Resolve([entry.Metadata], ["example"]).Success, "Dépendance Java absente ignorée.");
            using (var jar = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
                jar.CreateEntry("zombie/iso/IsoGridSquare.class");
            Assert(!JavaMods.Inspect(path, "42.20").Available, "JAR de remplacement accepté.");
        });
        Check("Java : agent embarqué, manifestes et JVM du jeu sans démarrer PZ", () =>
        {
            string install = InstallationLocator.Find("");
            var profile = new PlayerProfile { CachePath = Path.Combine(root, "java-empty"), JavaLoaderEnabled = true };
            var java = JavaMods.Prepare(profile, [], install);
            var vanilla = new GameLaunchService().CreateLaunchPlan(install, profile.AsGameProfile(), profile.AsJvmProfile());
            string output = JavaMods.PreflightAsync(vanilla, java).GetAwaiter().GetResult();
            Assert(output.Contains("Ready: 0 mods, 0 classes"), "Préflight réel non exécuté.");
            var plan = JavaMods.Attach(vanilla, java);
            Assert(plan.Arguments.Count(a => a.StartsWith("-javaagent:")) == 1 && plan.Arguments.Contains("-Dzomboid.steam=1"), "Agent mal intégré.");
            profile.JavaModIds.Add("missing-java-mod");
            var portable = ProfileSharing.Export(profile, "42.20", []);
            Assert(portable.Preferences.JavaLoaderEnabled && portable.Preferences.JavaModIds.Contains("missing-java-mod"), "Sélection Java non portable.");
        });
        JavaLoaderVerification.Run(root, Check);
        LoaderDownloadVerification.Run(root, Check);
        WorkshopVerification.Run(root, Check);
        InstallationVerification.Run(root, Check);
        OnlineVerification.Run(root, Check);
        BrowserWindowVerification.Run(Check);
        ServerProbeVerification.Run(Check);
        ServerStatisticsVerification.Run(Check);
        ManagementVerification.Run(root, Check);
        FtpWorldVerification.Run(root, Check);
        ConfigurationBooleanVerification.Run(root, Check);
        ServerLaunchScriptVerification.Run(Check, root);
        RuntimePackVerification.Run(root, Check);
        ServerJavaVerification.Run(root, Check);
        UsabilityVerification.Run(root, Check);
        JvmMapVerification.Run(root, Check);
        JvmLaunchVerification.Run(root, Check);
        string report = Path.Combine(LauncherStorage.Root, "verification.json");
        Directory.CreateDirectory(LauncherStorage.Root);
        File.WriteAllText(report, JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now, failures, fixtures = root, results }, new JsonSerializerOptions { WriteIndented = true }));
        LauncherStorage.Log("Vérification terminée : " + failures + " échec(s). " + report);
        return failures == 0 ? 0 : 1;
    }
}
