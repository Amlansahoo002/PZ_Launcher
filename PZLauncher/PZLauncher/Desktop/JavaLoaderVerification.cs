using System.IO.Compression;
using System.Text.Json;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class JavaLoaderVerification
{
    internal static void Jar(string path, params (string name, string text)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var jar = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(jar.CreateEntry(entry.name).Open());
            writer.Write(entry.text);
        }
    }
    internal static void Run(string root, Action<string, Action> check)
    {
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        string directory = Path.Combine(root, "java setups with spaces");
        Directory.CreateDirectory(directory);
        string patchA = Path.Combine(directory, "first.jar"), patchB = Path.Combine(directory, "second.jar");
        Jar(patchA, ("fixtures/Value.class", "first"));
        Jar(patchB, ("fixtures/Value.class", "second"));
        var first = JavaMods.Inspect(patchA, "42.20");
        var second = JavaMods.Inspect(patchB, "42.20");
        var profile = new PlayerProfile { CachePath = directory, JavaLoader = JavaMods.None, JavaLoaderEnabled = true };
        JavaMods.SetSelected(profile, [first.Id, second.Id]);
        string javaExe = Path.Combine(InstallationLocator.Find(""), "jre64", "bin", "java.exe");
        string vanillaJar = Path.Combine(directory, "vanilla.jar");
        Jar(vanillaJar, ("fixtures/Main.class", "main"));
        var vanilla = new LaunchPlan
        {
            WorkingDirectory = directory, CacheDirectory = directory, JavaExecutable = javaExe,
            ConsoleLogPath = Path.Combine(directory, "console.txt"),
            Arguments = ["-Djava.awt.headless=true", "--enable-native-access=ALL-UNNAMED", "-javaagent:old.jar",
                "-agentlib:zbNative", "-agentpath:C:/old.dll", "-Dleaf.addMods=old.jar", "-cp", vanillaJar, "fixtures.Main", "argument with spaces"]
        };
        var plans = new Dictionary<string, LaunchPlan>();
        check("Java : no agent, JAR directs ordonnés et suppression des agents hérités", () =>
        {
            Assert(first.Available && first.Loader == JavaMods.None, "JAR de classes refusé.");
            var setup = JavaMods.Prepare(profile, [first, second], directory);
            var plan = JavaMods.Attach(vanilla, setup); plans["direct-first"] = plan;
            Assert(!plan.Arguments.Any(a => a.StartsWith("-javaagent:") || a.StartsWith("-agentlib:") || a.StartsWith("-agentpath:") || a.StartsWith("-Dleaf.")), "Un agent subsiste.");
            int cp = plan.Arguments.ToList().IndexOf("-cp");
            Assert(plan.Arguments[cp + 1].Split(Path.PathSeparator).SequenceEqual(new[] { patchA, patchB, vanillaJar }), "Priorité des patchs incorrecte.");
            Assert(plan.Arguments.Contains("--enable-native-access=ALL-UNNAMED") && plan.Arguments.Last() == "argument with spaces", "Arguments du jeu altérés.");
            Assert(setup.Report.Contains("fixtures/Value.class"), "Collision de classes non signalée.");
            JavaMods.SetOrder(profile, [second.Id, first.Id]);
            plans["direct-second"] = JavaMods.Attach(vanilla, JavaMods.Prepare(profile, [first, second], directory));
            Assert(plans["direct-second"].Arguments[cp + 1].StartsWith(patchB + Path.PathSeparator), "Ordre non appliqué.");
        });
        check("Java : choix par loader, migration et partage sans chemins locaux", () =>
        {
            profile.JavaLoader = JavaMods.Leaf; JavaMods.SetSelected(profile, ["example_leaf"]);
            profile.LeafLibraryPath = "C:/private/leaf"; profile.ZombieBuddyAgentPath = "C:/private/ZombieBuddy.jar";
            profile.JavaLoader = JavaMods.None;
            Assert(JavaMods.Selected(profile).SequenceEqual(new[] { first.Id, second.Id }), "Sélection écrasée en changeant de loader.");
            var exported = ProfileSharing.Export(profile, "42.20", []);
            ProfileSharing.Validate(exported);
            string json = JsonSerializer.Serialize(exported);
            Assert(!json.Contains("private") && !json.Contains("LeafLibraryPath"), "Chemins du runtime exportés.");
            var imported = JsonSerializer.Deserialize<ProfilePreferences>(JsonSerializer.Serialize(exported.Preferences))!;
            Assert(imported.JavaLoader == JavaMods.None && imported.JavaModOrder[JavaMods.None][0] == second.Id, "Mode ou ordre perdu.");
            var old = JsonSerializer.Deserialize<PlayerProfile>("{\"JavaLoaderEnabled\":true,\"JavaModIds\":[\"old_mod\"]}")!;
            Assert(old.JavaLoader == JavaMods.Community && JavaMods.Selected(old).Single() == "old_mod", "Ancien profil cassé.");
        });
        check("Java : Leaf identifié par descripteur, métadonnées invalides et runtime incomplet", () =>
        {
            string leafMod = Path.Combine(directory, "unrelated-name.jar");
            Jar(leafMod, ("leaf.mod.json", "{\"schemaVersion\":1,\"id\":\"example_leaf\",\"name\":\"Leaf example\",\"version\":\"1.0\",\"depends\":{\"leafloader\":\"*\"}}"));
            var mod = JavaMods.Inspect(leafMod, "42.20");
            Assert(mod.Available && mod.Loader == JavaMods.Leaf && mod.Id == "example_leaf", "Identification fondée sur le nom.");
            string bad = Path.Combine(directory, "broken.jar");
            Jar(bad, ("leaf.mod.json", "{\"schemaVersion\":1,\"id\":4}"));
            Assert(!JavaMods.Inspect(bad, "42.20").Available, "JSON mal typé accepté.");
            profile.JavaLoader = JavaMods.Leaf; JavaMods.SetSelected(profile, [mod.Id]);
            string lib = Path.Combine(directory, ".leaf", "lib"); Directory.CreateDirectory(lib);
            profile.LeafLibraryPath = lib;
            bool rejected = false;
            try { JavaMods.Prepare(profile, [mod], directory); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Bibliothèques absentes acceptées.");
            Jar(Path.Combine(lib, "runtime.jar"),
                ("dev/aoqia/leaf/loader/impl/launch/knot/KnotClient.class", "fixture"),
                ("org/objectweb/asm/ClassReader.class", "fixture"), ("org/objectweb/asm/tree/ClassNode.class", "fixture"),
                ("org/objectweb/asm/commons/Remapper.class", "fixture"), ("org/objectweb/asm/tree/analysis/Analyzer.class", "fixture"),
                ("org/objectweb/asm/util/CheckClassAdapter.class", "fixture"), ("org/spongepowered/asm/launch/MixinBootstrap.class", "fixture"));
            var plan = JavaMods.Attach(vanilla, JavaMods.Prepare(profile, [mod], directory));
            plans["leaf"] = plan;
            Assert(plan.Arguments.Contains("dev.aoqia.leaf.loader.impl.launch.knot.KnotClient") && !plan.Arguments.Contains("fixtures.Main"), "Mauvais point d'entrée Leaf.");
            Assert(!plan.Arguments.Any(a => a.StartsWith("-javaagent:")) && plan.Arguments.Any(a => a.StartsWith("-Dleaf.addMods=@")), "Leaf traité comme un agent.");
        });
        check("Java : ZombieBuddy, manifeste plié, agent unique et interface d'approbation", () =>
        {
            string agent = Path.Combine(directory, "arbitrary-agent.jar");
            Jar(agent, ("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\nPremain-Class: me.zed_0xff.zombie_\n buddy.Agent\n\n"),
                ("me/zed_0xff/zombie_buddy/Agent.class", "fixture"));
            profile.JavaLoader = JavaMods.ZombieBuddy; profile.ZombieBuddyAgentPath = agent;
            var plan = JavaMods.Attach(vanilla, JavaMods.Prepare(profile, [], directory)); plans["zombiebuddy"] = plan;
            Assert(plan.Arguments.Count(a => a.StartsWith("-javaagent:")) == 1 && plan.Arguments.Contains("-javaagent:" + agent), "Mauvais agent sélectionné.");
            Assert(plan.Arguments.Contains("-Djava.awt.headless=false") && !plan.Arguments.Contains("-Djava.awt.headless=true"), "Interface de ZombieBuddy neutralisée.");
            Assert(!plan.Arguments.Any(a => a.Contains("policy=")), "Politique ZombieBuddy changée.");
            profile.ZombieBuddyAgentPath = patchA;
            bool rejected = false; try { JavaMods.Prepare(profile, [], directory); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Un patch de classe est accepté comme agent.");
        });
        check("Java : association mod.info, version/common et désactivation du propriétaire", () =>
        {
            string owner = Path.Combine(directory, "mods", "buddy-owner");
            Directory.CreateDirectory(Path.Combine(owner, "common")); Directory.CreateDirectory(Path.Combine(owner, "42"));
            File.WriteAllText(Path.Combine(owner, "common", "mod.info"), "id=buddy-owner\njavaJarFile=media/java/patch.jar\njavaPkgName=example.patch\n");
            Jar(Path.Combine(owner, "42", "media", "java", "patch.jar"), ("example/patch/Patch.class", "fixture"));
            var traditional = new InstalledMod { Id = "buddy-owner", Name = "Buddy owner", Location = owner, Available = true, Target = "B42" };
            profile.JavaLoader = JavaMods.ZombieBuddy;
            var catalog = JavaMods.Scan(profile, [traditional], [traditional.Id], directory);
            var associated = catalog.Single(m => m.Owner == traditional.Id);
            Assert(associated.Available && associated.Loader == JavaMods.ZombieBuddy && associated.Path.Contains(Path.Combine("42", "media")), "Association common/version incorrecte.");
            Assert(JavaMods.Requested(profile, catalog).Contains(associated.Id), "JAR lié non sélectionné.");
            catalog = JavaMods.Scan(profile, [traditional], [], directory);
            Assert(!JavaMods.Requested(profile, catalog).Contains(associated.Id), "JAR lié actif après désactivation du mod.");
            File.Delete(associated.Path);
            catalog = JavaMods.Scan(profile, [traditional], [traditional.Id], directory);
            Assert(catalog.Single(m => m.Owner == traditional.Id).Issue.Length > 0, "JAR déclaré manquant ignoré.");
        });
        File.WriteAllText(Path.Combine(root, "loader-launch-plans.json"), JsonSerializer.Serialize(plans, new JsonSerializerOptions { WriteIndented = true }));
    }
}
