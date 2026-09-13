using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PZLauncher.Desktop;

internal static class LoaderDownloadVerification
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
    }
    private sealed class Reporting(Action<LoaderTransfer> action) : IProgress<LoaderTransfer>
    { public void Report(LoaderTransfer value) => action(value); }
    private static byte[] Zip(params (string Name, string Data)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open()); writer.Write(entry.Data); }
        return memory.ToArray();
    }
    private static string Releases(string loader, byte[] asset)
    {
        string repo = CommunityLinks.Repository(loader), name = loader == JavaMods.Leaf ? "loader-1.2.3.jar" : "ZombieBuddy.jar";
        return JsonSerializer.Serialize(new object[] { new { tag_name = "windows_installer_9", draft = false, prerelease = false, assets = Array.Empty<object>() },
            new { tag_name = "v1.2.3", draft = false, prerelease = false, assets = new[] {
                new { name, browser_download_url = $"https://github.com/{repo}/releases/download/v1.2.3/{name}", size = asset.Length,
                    digest = "sha256:" + Convert.ToHexString(SHA256.HashData(asset)).ToLowerInvariant() } } } });
    }
    internal static void Run(string fixtures, Action<string, Action> check)
    {
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        string root = Path.Combine(fixtures, "loader-downloads"); Directory.CreateDirectory(root);
        check("Java : gros manifeste signé, attributs repliés et limite du seul en-tête", () =>
        {
            byte[] bytes = Zip(("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\r\nPremain-Class: me.zed_0xff.zombie_\r\n buddy.Agent\r\n\r\n" +
                string.Concat(Enumerable.Repeat("Name: fixture/Test.class\r\nSHA-256-Digest: unused\r\n\r\n", 25000))));
            using var memory = new MemoryStream(bytes); using var jar = new ZipArchive(memory);
            var attributes = JavaMods.Manifest(jar);
            Assert(attributes["Premain-Class"] == "me.zed_0xff.zombie_buddy.Agent" && !attributes.ContainsKey("Name"), "Signed JAR main section was rejected or mixed with signature sections.");
            using var oversized = new MemoryStream(Zip(("META-INF/MANIFEST.MF", "Premain-Class: " + new string('x', 262145))));
            using var invalid = new ZipArchive(oversized); bool rejected = false;
            try { JavaMods.Manifest(invalid); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Unbounded manifest header accepted.");
        });
        byte[] dependency = Zip(("fixture/Dependency.class", "fixture"));
        string manifest = JsonSerializer.Serialize(new { mainClass = new { client = "dev.aoqia.leaf.loader.impl.launch.knot.KnotClient" },
            libraries = new { common = new[] { new { name = "test:library:1.0", url = "https://example.test/", size = dependency.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(dependency)) } }, client = Array.Empty<object>() } });
        byte[] leaf = Zip(("leaf-installer.json", manifest), ("dev/aoqia/leaf/loader/impl/launch/knot/KnotClient.class", "fixture"),
            ("org/objectweb/asm/ClassReader.class", "fixture"), ("org/objectweb/asm/tree/ClassNode.class", "fixture"),
            ("org/objectweb/asm/commons/Remapper.class", "fixture"), ("org/objectweb/asm/tree/analysis/Analyzer.class", "fixture"),
            ("org/objectweb/asm/util/CheckClassAdapter.class", "fixture"), ("org/spongepowered/asm/launch/MixinBootstrap.class", "fixture"));
        byte[] buddy = Zip(("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\nPremain-Class: me.zed_0xff.zombie_buddy.Agent\n\n"),
            ("me/zed_0xff/zombie_buddy/Agent.class", "fixture"));
        byte[] companion = Zip(("repo/42/mod.info", "name=ZombieBuddy\nid=ZombieBuddy\njavaJarFile=../libs/ZombieBuddy.jar\njavaPkgName=me.zed_0xff.zombie_buddy\n"),
            ("repo/common/media/lua/client/test.lua", "return 1"), ("repo/LICENSE.txt", "fixture license"), ("repo/java/build.gradle", "not installed"));
        bool corrupt = false;
        using var client = new HttpClient(new Handler(request =>
        {
            string uri = request.RequestUri!.AbsoluteUri;
            bool isBuddy = uri.Contains("ZombieBuddy");
            byte[] bytes = uri.Contains("/releases?") ? Encoding.UTF8.GetBytes(Releases(isBuddy ? JavaMods.ZombieBuddy : JavaMods.Leaf, isBuddy ? buddy : leaf))
                : uri.Contains("/zipball/") ? companion : uri.Contains("example.test") ? dependency : isBuddy ? buddy : leaf;
            if (corrupt && !uri.Contains("/releases?")) { bytes = [.. bytes]; bytes[^1] ^= 1; }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        using var downloads = new JavaLoaderDownloads(client);
        var leafRelease = JavaLoaderDownloads.ParseReleases(JavaMods.Leaf, Releases(JavaMods.Leaf, leaf)).Single();
        var buddyRelease = JavaLoaderDownloads.ParseReleases(JavaMods.ZombieBuddy, Releases(JavaMods.ZombieBuddy, buddy)).Single();
        string installation = InstallationLocator.Find("");
        LoaderInstallation? first = null;
        check("Loaders : versions GitHub filtrées et dépendances Leaf installées par profil", () =>
        {
            Assert(buddyRelease.Tag == "v1.2.3", "Installer EXE selected as a runtime release.");
            int progress = 0;
            first = downloads.InstallAsync(leafRelease, root, installation, new Reporting(_ => progress++), CancellationToken.None).GetAwaiter().GetResult();
            Assert(File.Exists(Path.Combine(first.RuntimePath, "library-1.0.jar")) && progress > 2 && first.RuntimePath.StartsWith(root), "Dependencies, progress or isolation missing.");
            Assert(File.Exists(Path.Combine(first.Directory, "installation.json")), "Installed provenance missing.");
        });
        check("Loaders : refus de hash incorrect et annulation conservent la version précédente", () =>
        {
            string hash = WorldFiles.Hash(Path.Combine(first!.RuntimePath, "loader-1.2.3.jar"));
            corrupt = true; bool rejected = false;
            try { downloads.InstallAsync(leafRelease, root, installation, null, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { rejected = true; } finally { corrupt = false; }
            Assert(rejected && WorldFiles.Hash(Path.Combine(first.RuntimePath, "loader-1.2.3.jar")) == hash, "Hash failure replaced the installed runtime.");
            using var cancellation = new CancellationTokenSource(); bool cancelled = false;
            try { downloads.InstallAsync(leafRelease, root, installation, new Reporting(_ => cancellation.Cancel()), cancellation.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && Directory.GetDirectories(Path.Combine(root, ".pzlauncher-loaders", "staging")).Length == 0, "Cancellation leaves staging or ignores the token.");
        });
        check("Loaders : ZombieBuddy complet, licence, libs et remplacement sauvegardé", () =>
        {
            string cache = Path.Combine(root, "buddy-profile");
            string mod = Path.Combine(cache, "mods", "ZombieBuddy"); Directory.CreateDirectory(mod);
            File.WriteAllText(Path.Combine(mod, "mod.info"), "id=ZombieBuddy\n"); File.WriteAllText(Path.Combine(mod, "old.txt"), "preserve me");
            LauncherStorage.WriteAtomic(WorkshopStore.IndexPath(cache), JsonSerializer.Serialize(new[] { new WorkshopInstall
            { ItemId = "3619862853", Mods = [new() { Folder = "ZombieBuddy", ModIds = ["ZombieBuddy"] }] } }));
            var result = downloads.InstallAsync(buddyRelease, cache, installation, null, CancellationToken.None).GetAwaiter().GetResult();
            Assert(File.Exists(result.RuntimePath) && File.Exists(Path.Combine(mod, "libs", "ZombieBuddy.jar")) && File.Exists(Path.Combine(mod, "LICENSE.txt")), "Incomplete companion/runtime package.");
            Assert(!Directory.Exists(Path.Combine(mod, "java")) && WorkshopStore.Read(cache).Count == 0, "Build sources or stale Workshop revision retained.");
            Assert(Directory.GetFiles(Path.Combine(cache, ".pzlauncher-loaders", "backups"), "old.txt", SearchOption.AllDirectories).Length == 1, "Previous mod was not backed up.");
            var catalog = ModCatalog.Scan(new() { CachePath = cache }, new(42, 20));
            Assert(catalog.Any(m => m.Id == "ZombieBuddy" && m.Available), "Companion mod cannot be detected.");
        });
        check("Loaders : archive traversante refusée et transaction restaurée après erreur", () =>
        {
            string zip = Path.Combine(root, "invalid.zip"), target = Path.Combine(root, "extract"); Directory.CreateDirectory(target);
            File.WriteAllBytes(zip, Zip(("repo/42/mod.info", "id=ZombieBuddy"), ("repo/42/../../escape.txt", "bad")));
            bool rejected = false; try { JavaLoaderDownloads.ExtractCompanion(zip, target, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected && !File.Exists(Path.Combine(root, "escape.txt")), "Archive escaped the extraction root.");
            string cache = Path.Combine(root, "rollback"), control = Path.Combine(cache, ".pzlauncher-loaders"), staging = Path.Combine(control, "staging", "fixture");
            string mod = Path.Combine(cache, "mods", "ZombieBuddy"); Directory.CreateDirectory(mod); File.WriteAllText(Path.Combine(mod, "mod.info"), "id=ZombieBuddy\n");
            File.WriteAllText(Path.Combine(mod, "old.txt"), "unchanged");
            string fresh = Path.Combine(staging, "companion"); Directory.CreateDirectory(fresh); File.WriteAllText(Path.Combine(fresh, "mod.info"), "id=ZombieBuddy\n");
            Directory.CreateDirectory(Path.Combine(staging, "runtime")); Directory.CreateDirectory(Path.Combine(staging, "installation.json"));
            bool failed = false;
            try { JavaLoaderDownloads.Commit(buddyRelease, cache, control, staging, CancellationToken.None); } catch (UnauthorizedAccessException) { failed = true; }
            Assert(failed && File.ReadAllText(Path.Combine(mod, "old.txt")) == "unchanged" && Directory.Exists(staging), "Failed commit did not restore the previous mod/runtime.");
        });
    }
    internal static int Smoke()
    {
        string root = Path.Combine(Path.GetTempPath(), "PZLauncher-loader-download-" + Guid.NewGuid().ToString("N"));
        var results = new List<object>();
        try
        {
            Directory.CreateDirectory(root); string installation = InstallationLocator.Find("");
            using var downloads = new JavaLoaderDownloads();
            foreach (string loader in new[] { JavaMods.Leaf, JavaMods.ZombieBuddy })
            {
                var release = downloads.ReleasesAsync(loader, CancellationToken.None).GetAwaiter().GetResult().First();
                int reports = 0;
                var result = downloads.InstallAsync(release, Path.Combine(root, loader), installation,
                    new Reporting(_ => Interlocked.Increment(ref reports)), CancellationToken.None).GetAwaiter().GetResult();
                results.Add(new { result, progressReports = reports, jars = Directory.GetFiles(Path.GetDirectoryName(result.RuntimePath.EndsWith(".jar") ? result.RuntimePath : Path.Combine(result.RuntimePath, "dummy"))!, "*.jar").Length });
            }
            File.WriteAllText(Path.Combine(LauncherStorage.Root, "loader-download-verification.json"), JsonSerializer.Serialize(new { passed = true, root, results }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(LauncherStorage.Root, "loader-download-verification.json"), JsonSerializer.Serialize(new { passed = false, root, results, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }
}
