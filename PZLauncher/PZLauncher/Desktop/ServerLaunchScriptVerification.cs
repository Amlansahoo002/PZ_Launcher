using System.Text;
using System.Text.Json;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class ServerLaunchScriptVerification
{
    internal static void Run(Action<string, Action> check, string root)
    {
        string directory = Path.Combine(root, "script-export"), installation = Path.Combine(directory, "jeu été & 50% !"), cache = Path.Combine(directory, "cache");
        Directory.CreateDirectory(installation); Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(installation, "projectzomboid.jar"), "fixture");
        LaunchPlan Plan(params string[] extra) => new()
        {
            JavaExecutable = Path.Combine(installation, "jre64", "bin", "java.exe"), WorkingDirectory = installation, CacheDirectory = cache,
            Arguments = extra.Concat(["-Xms1024m", "-Xmx4096m", "-XX:+UseG1GC", "-XX:MaxGCPauseMillis=125", "-XX:+UseStringDeduplication",
                "-Djava.library.path=./natives/;./natives/win64/;./", "-Dzomboid.steam=0", "-cp", ".;projectzomboid.jar", "zombie.network.GameServer",
                "-cachedir=" + cache, "-servername", "fixture", "-nosteam"]).ToArray()
        };
        void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        check("Scripts serveur : BAT/SH, JVM préservée, chemins Linux et fins de ligne", () =>
        {
            string target = Path.Combine(directory, "both");
            var plan = Plan(); ServerLaunchScripts.Export(plan, target, "fixture", true, true, "/home/pz/Sauvegardes été");
            string batch = File.ReadAllText(Path.Combine(target, "fixture.bat")), shell = File.ReadAllText(Path.Combine(target, "fixture.sh"));
            using var windows = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "fixture.assets", "windows.json")));
            string linuxArgs = File.ReadAllText(Path.Combine(target, "fixture.assets", "linux.args"));
            Assert(windows.RootElement.GetProperty("arguments").EnumerateArray().Select(v => v.GetString()).SequenceEqual(plan.Arguments), "Vanilla Windows plan changed.");
            Assert(batch.Contains("DisableDelayedExpansion") && batch.Contains("%~dp0fixture.assets\\run-windows.ps1") && !batch.Contains(installation), "Batch paths are shell-expanded.");
            Assert(!shell.Contains('\r') && !shell.Contains("java.exe") && shell.Contains("jre64/bin/java") && shell.Contains("LD_LIBRARY_PATH"), "Linux script contains Windows runtime instructions.");
            Assert(linuxArgs.Contains(".:projectzomboid.jar") && linuxArgs.Contains("-cachedir=/home/pz/Sauvegardes été") && !linuxArgs.Contains("win64") && !linuxArgs.Contains(cache), "Linux arguments still use Windows paths.");
            Assert(linuxArgs.Contains("-XX:MaxGCPauseMillis=125") && linuxArgs.Contains("zombie.network.GameServer") && linuxArgs.Contains("-nosteam"), "JVM or server arguments lost.");
        });
        check("Scripts serveur : manifests API 1 et JAR figés, chemins relatifs Linux", () =>
        {
            string agent = Path.Combine(directory, "agent.jar"), mod = Path.Combine(directory, "mod.jar"), manifest = Path.Combine(directory, "launch.properties");
            File.WriteAllText(agent, "agent"); File.WriteAllText(mod, "mod-before");
            string B64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
            File.WriteAllText(manifest, "format=1\nside=server\ngameJar=" + B64(Path.Combine(installation, "projectzomboid.jar")) + "\nmodCount=1\nmod.0.path=" + B64(mod) + "\nmod.0.sha256=fixture\n");
            string target = Path.Combine(directory, "community");
            ServerLaunchScripts.Export(Plan("-javaagent:" + agent + "=" + manifest), target, "fixture", false, true, "/home/pz/Zomboid");
            string exported = File.ReadAllText(Path.Combine(target, "fixture.assets", "linux", "launch.properties"));
            Assert(exported.Contains("gameJar=" + B64("projectzomboid.jar")) && exported.Contains("mod.0.sha256=fixture"), "Game path or integrity check lost.");
            string mapped = Encoding.UTF8.GetString(Convert.FromBase64String(exported.Split('\n').Single(l => l.StartsWith("mod.0.path="))["mod.0.path=".Length..]));
            File.WriteAllText(mod, "mod-after");
            Assert(mapped.StartsWith("./fixture.assets/linux/") && File.ReadAllText(Path.Combine(target, mapped)) == "mod-before", "Java mod snapshot not independent.");
            Assert(!Directory.EnumerateFiles(target, "*.bat").Any(), "SH-only selection also exported BAT.");
        });
        check("Scripts serveur : sélection Leaf, main serveur et quoting des arguments", () =>
        {
            string jar = Path.Combine(directory, "leaf.jar"), list = Path.Combine(directory, "leaf.txt");
            File.WriteAllText(jar, "leaf"); File.WriteAllText(list, jar + "\n");
            var source = Plan("-Dleaf.addMods=@" + list, "-Dleaf.gamePath=" + installation);
            var args = source.Arguments.ToArray(); args[Array.IndexOf(args, "-cp") + 2] = "dev.aoqia.leaf.loader.impl.launch.knot.KnotServer";
            var plan = new LaunchPlan { JavaExecutable = source.JavaExecutable, WorkingDirectory = installation, CacheDirectory = cache, Arguments = args };
            string target = Path.Combine(directory, "leaf"); ServerLaunchScripts.Export(plan, target, "fixture", false, true, "/home/pz/Zomboid");
            string text = File.ReadAllText(Path.Combine(target, "fixture.assets", "linux.args"));
            string selection = File.ReadAllText(Path.Combine(target, "fixture.assets", "linux", "leaf-mods.txt"));
            Assert(text.Contains("KnotServer") && text.Contains("-Dleaf.gamePath=.") && selection.StartsWith("./fixture.assets/linux/"), "Leaf selection or server main changed.");
            string quoted = ServerLaunchScripts.ArgumentFile(["a b", "a\"b", "tail\\", "100% ! & | < > ^ $() ` #", ""]);
            Assert(quoted.Contains("\"a\\\"b\"") && quoted.Contains("\"tail\\\\\"") && quoted.EndsWith("\"\"\n"), "Java argument-file quoting changed.");
        });
        check("Scripts serveur : destination existante et mauvais cache Linux refusés", () =>
        {
            void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } catch (IOException) { return; } throw new InvalidOperationException("Unsafe export accepted."); }
            Reject(() => ServerLaunchScripts.Export(Plan(), directory, "fixture", true, false, ""));
            Reject(() => ServerLaunchScripts.Export(Plan(), Path.Combine(directory, "bad-cache"), "fixture", false, true, @"C:\Zomboid"));
            Reject(() => ServerLaunchScripts.Export(Plan(), Path.Combine(directory, "no-format"), "fixture", false, false, ""));
            Assert(!Directory.Exists(Path.Combine(directory, "bad-cache")), "Invalid export wrote files.");
        });
    }
}
