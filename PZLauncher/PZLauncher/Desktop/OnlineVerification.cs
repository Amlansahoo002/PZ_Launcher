using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class OnlineVerification
{
    internal static void Run(string root, Action<string, Action> check)
    {
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Reject(Action action)
        {
            bool failed = false; try { action(); } catch (InvalidDataException) { failed = true; }
            Assert(failed, "Invalid input was accepted.");
        }
        string data = Path.Combine(root, "online");
        Directory.CreateDirectory(data);
        var basis = new PlayerProfile { CachePath = Path.Combine(data, "solo"), JavaLoaderEnabled = true, JavaModIds = ["unrelated"] };
        Directory.CreateDirectory(basis.CachePath);
        File.WriteAllText(Path.Combine(basis.CachePath, "options.ini"), "width=1280\n");
        var state = new LauncherState { Profiles = [basis] };
        var a = new OnlineFavorite { Name = "Server A", Host = "EXAMPLE.test", WorkshopIds = ["123"], ModIds = ["OnlineExample"], Password = "session-secret" };
        var b = new OnlineFavorite { Name = "Server B", Host = "192.0.2.20", Port = 16500, QueryPort = 16501, WorkshopIds = ["123"], ModIds = ["OnlineExample"] };
        PlayerProfile? pa = null, pb = null;
        check("Online : favoris persistants, caches séparés, paramètres copiés et secrets exclus", () =>
        {
            pa = OnlineProfiles.Save(state, a, basis, data); pb = OnlineProfiles.Save(state, b, basis, data);
            Assert(pa.CachePath != pb.CachePath && pa.CachePath != basis.CachePath && pa.Id != pb.Id, "Profiles share a cache.");
            Assert(File.ReadAllText(Path.Combine(pa.CachePath, "options.ini")) == "width=1280\n" && !pa.JavaLoaderEnabled && pa.JavaModIds.Count == 0, "Profile copied unrelated Java selections.");
            string json = JsonSerializer.Serialize(state);
            Assert(!json.Contains("session-secret") && json.Contains("example.test") && json.Contains("OnlineServerId"), "Favorite persistence or password privacy failed.");
            var restored = JsonSerializer.Deserialize<LauncherState>(json)!;
            Assert(restored.OnlineFavorites.Count == 2 && restored.OnlineFavorites[1].ProfileId == pb.Id, "Favorite/profile association lost.");
            OnlineProfiles.Save(state, a, basis, data);
            Assert(state.Profiles.Count == 3, "Editing a favorite created another profile.");
        });
        check("Online : validation IPv4/DNS/ports et commandes +connect/+password isolées", () =>
        {
            foreach (string host in new[] { "", "host:16261", "::1", "host +quit", "https://example.test", "host\nquit" })
                Reject(() => OnlineProfiles.Host(host));
            Reject(() => OnlineProfiles.Validate(new() { Name = "bad", Host = "localhost", Port = 0 }));
            Assert(OnlineProfiles.Host("LOCALHOST") == "localhost", "DNS host normalization failed.");
            pa!.Launch.ModFolders = "steam";
            string installation = Path.Combine(data, "game"); Directory.CreateDirectory(installation);
            File.WriteAllText(Path.Combine(installation, "ProjectZomboid64.json"), """{"vmArgs":[],"classpath":["projectzomboid.jar"],"mainClass":"zombie/gameStates/MainScreenState"}""");
            var plan = new GameLaunchService().CreateLaunchPlan(installation, pa.AsGameProfile(), pa.AsJvmProfile());
            int index = plan.Arguments.ToList().IndexOf("+connect");
            Assert(index >= 0 && plan.Arguments[index + 1] == "example.test:16261", "Wrong direct connection.");
            Assert(plan.Arguments.Contains("-modfolders") && plan.Arguments.Last() == "mods" && !plan.CommandLinePreview.Contains("session-secret"), "Isolation or redaction failed.");
            Assert(OnlineProfiles.SameVersion(new(42, 20), new(42, 20, 0)) && !OnlineProfiles.SameVersion(new(42, 20), new(42, 21)), "PZ version comparison incorrect.");
        });
        check("Online : décodage Steamworks x64, UTF-8 et ports distincts", () =>
        {
            var bytes = new byte[364];
            void Int(int at, int value) => BitConverter.GetBytes(value).CopyTo(bytes, at);
            BitConverter.GetBytes((ushort)16261).CopyTo(bytes, 0); BitConverter.GetBytes((ushort)27016).CopyTo(bytes, 2);
            Int(4, unchecked((int)0xC0000211)); Int(8, 35); bytes[12] = 1; Int(144, 108600); Int(148, 12); Int(152, 32);
            Encoding.UTF8.GetBytes("Serveur été").CopyTo(bytes, 172); Encoding.UTF8.GetBytes(";modded;VERSION:42.20.0").CopyTo(bytes, 236);
            var parsed = NativeSteamBrowser.ParseInfo(bytes)!;
            Assert(parsed.Name == "Serveur été" && parsed.Host == "192.0.2.17" && parsed.Port == 16261 && parsed.QueryPort == 27016 && parsed.Version == "42.20.0" && parsed.Players == 12, "Steam structure layout incorrect.");
            Int(144, 730); Assert(NativeSteamBrowser.ParseInfo(bytes) == null, "Another game's server was accepted.");
        });
        check("Online : initialisation Steam moderne sans export Init, repli ancien et erreurs détaillées", () =>
        {
            int calls = 0;
            NativeSteamBrowser.InitFlat modern = _ => { calls++; return 0; };
            NativeSteamBrowser.Init legacy = () => { calls++; return 1; };
            nint modernPointer = Marshal.GetFunctionPointerForDelegate(modern), legacyPointer = Marshal.GetFunctionPointerForDelegate(legacy);
            string selected = NativeSteamBrowser.InitializeSteam(name => name == "SteamAPI_InitFlat" ? modernPointer : 0, "fixture.dll");
            Assert(selected == "SteamAPI_InitFlat" && calls == 1, "Modern DLL without SteamAPI_Init failed or success code misread.");
            foreach (string export in new[] { "SteamAPI_Init", "SteamAPI_InitSafe" })
                Assert(NativeSteamBrowser.InitializeSteam(name => name == export ? legacyPointer : 0, "fixture.dll") == export, "Legacy DLL fallback failed.");
            NativeSteamBrowser.InitFlat failure = ptr =>
            {
                byte[] text = Encoding.UTF8.GetBytes("Client indisponible — test\0"); Marshal.Copy(text, 0, ptr, text.Length); return 2;
            };
            bool rejected = false;
            try { NativeSteamBrowser.InitializeSteam(name => name == "SteamAPI_InitFlat" ? Marshal.GetFunctionPointerForDelegate(failure) : legacyPointer, "fixture.dll"); }
            catch (IOException ex) { rejected = ex.Message.Contains("Client indisponible — test") && ex.Message.Contains('2'); }
            Assert(rejected && calls == 3, "Steam initialization failure was hidden or retried through another API.");
            rejected = false;
            try { NativeSteamBrowser.InitializeSteam(_ => 0, "fixture.dll"); }
            catch (EntryPointNotFoundException ex) { rejected = ex.Message.Contains("fixture.dll") && ex.Message.Contains("SteamAPI_InitFlat"); }
            Assert(rejected, "Missing export diagnostic does not name the DLL and entry points.");
            GC.KeepAlive(modern); GC.KeepAlive(legacy); GC.KeepAlive(failure);
        });
        check("Online : listes publiques tronquées et import des seuls champs de mods INI", () =>
        {
            var info = new OnlineServerInfo { Rules = new() { ["mods"] = @"\One;\Two", ["modCount"] = "3" } };
            Assert(!info.CompleteModList && info.ModIds.SequenceEqual(new[] { "One", "Two" }), "Truncated public list treated as complete.");
            info.Rules["modCount"] = "2"; Assert(info.CompleteModList, "Complete list not recognized.");
            info.Rules.Remove("modCount"); Assert(!info.CompleteModList, "Missing count treated as verified.");
            var parsed = OnlineProfiles.ParseServerIni("Password=not-imported\nMods=\\One;\\Two\nWorkshopItems=123;456\n");
            Assert(parsed.Mods.SequenceEqual(new[] { "One", "Two" }) && parsed.Workshop.SequenceEqual(new[] { "123", "456" }), "Server INI import failed.");
            Reject(() => OnlineProfiles.ParseServerIni("Mods=One"));
        });
        string library = Path.Combine(data, "steam");
        string workshop = Path.Combine(library, "steamapps", "workshop");
        string source = Path.Combine(workshop, "content", "108600", "123", "mods", "OnlineMod", "42");
        string manifest = Path.Combine(workshop, "appworkshop_108600.acf");
        string Acf(long updated, string revision) => "\"AppWorkshop\" { \"appid\" \"108600\" \"WorkshopItemsInstalled\" { \"123\" { \"manifest\" \"" + revision + "\" \"timeupdated\" \"" + updated + "\" } } \"WorkshopItemDetails\" { \"123\" { \"timeupdated\" \"9999\" } } }";
        check("Online : copie d’une révision Steam et conservation lors d’une mise à jour globale", () =>
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "mod.info"), "id=OnlineExample\nname=Online example\n");
            File.WriteAllText(Path.Combine(source, "version.txt"), "old");
            File.WriteAllText(manifest, Acf(100, "1000"));
            var first = OnlineMods.CopyInstalledAsync(a, pa!, CancellationToken.None, [library]).GetAwaiter().GetResult();
            Assert(first.Single().Success, first.Single().Detail);
            File.WriteAllText(Path.Combine(source, "version.txt"), "new");
            File.WriteAllText(manifest, Acf(200, "2000"));
            OnlineMods.CopyInstalledAsync(a, pa!, CancellationToken.None, [library]).GetAwaiter().GetResult();
            OnlineMods.CopyInstalledAsync(b, pb!, CancellationToken.None, [library]).GetAwaiter().GetResult();
            string FileFor(PlayerProfile p) => Path.Combine(p.CachePath, "mods", "OnlineMod", "42", "version.txt");
            Assert(File.ReadAllText(FileFor(pa!)) == "old" && File.ReadAllText(FileFor(pb!)) == "new", "Server copies leaked another server's update.");
            Assert(WorkshopStore.Read(pa!.CachePath).Single().WorkshopUpdated == 100 && WorkshopStore.Read(pb!.CachePath).Single().Manifest == "2000", "Installed revision not retained.");
            var mods = ModCatalog.Scan(pa, new Version(42, 20));
            Assert(mods.Count == 1 && mods[0].Id == "OnlineExample" && mods[0].Location.StartsWith(pa.CachePath), "Global Workshop files leaked into online catalog.");
        });
        check("Online : état des mises à jour fondé sur la révision installée, jamais sur la date de copie", () =>
        {
            var remote = new Dictionary<string, WorkshopPublished> { ["123"] = new("123", "Online example", 200, true) };
            Assert(OnlineMods.Compare(a, pa!.CachePath, remote).Single().Status == "update", "Old snapshot not reported.");
            Assert(OnlineMods.Compare(b, pb!.CachePath, remote).Single().Status == "current", "Current snapshot not recognized.");
            Assert(OnlineMods.Compare(a, pa.CachePath, new Dictionary<string, WorkshopPublished>()).Single().Status == "unknown", "Unavailable metadata treated as current.");
            var absent = new OnlineFavorite { WorkshopIds = ["456"] };
            Assert(OnlineMods.Compare(absent, pa.CachePath, remote).Single().Status == "missing", "Missing item not reported.");
            string response = """{"response":{"publishedfiledetails":[{"publishedfileid":"123","result":1,"consumer_app_id":108600,"time_updated":200,"title":"Mod"},{"publishedfileid":"456","result":1,"consumer_app_id":730,"time_updated":200},{"publishedfileid":"789","result":9}]}}""";
            var metadata = OnlineMods.ParsePublished(response, ["123", "456", "789"]);
            Assert(metadata[0].Available && !metadata[1].Available && !metadata[2].Available, "Wrong-app/inaccessible metadata trusted.");
            Assert(WorkshopVersions.ParseInstalled(Acf(100, "1000"), "123").Updated == 100, "Pending Steam revision used as installed.");
            Reject(() => WorkshopVersions.ParseInstalled("\"AppWorkshop\" { \"appid\" \"108600\"", "123"));
        });
        check("Online : édition des listes, nouveau favori et profil séparé dans les contrôles WinForms", () =>
        {
            using var window = new LauncherWindow(verification: true);
            window.VerifyOnlineControls(Path.Combine(data, "ui"));
        });
    }
}

internal sealed partial class LauncherWindow
{
    internal void VerifyOnlineControls(string root)
    {
        profile = new() { Name = "UI fixture", CachePath = root };
        state.Profiles = [profile]; state.OnlineFavorites = [];
        selection = new(root); onlineSelectedId = "new"; onlineDraft = new() { Name = "First", Host = "localhost" };
        ShowPage("Online");
        var inputs = Descendants(pageHost).OfType<TextBox>().ToList();
        inputs.Single(t => t.Name == "OnlineWorkshopIds").Text = "123;456";
        inputs.Single(t => t.Name == "OnlineModIds").Text = "\\One;\\Two";
        if (!SaveOnlineFavorite() || state.OnlineFavorites.Single().WorkshopIds.Count != 2 || state.OnlineFavorites.Single().ModIds[0] != "One")
            throw new InvalidOperationException("Saving from another tab lost edited lists.");
        onlineSelectedId = "new"; onlineDraft = new(); onlineDirty = false; ShowPage("Online");
        if (onlineDraft!.Name.Length != 0) throw new InvalidOperationException("New favorite reverted to the existing favorite.");
        var first = state.OnlineFavorites.Single(); onlineSelectedId = first.Id; onlineDraft = CloneFavorite(first); ShowPage("Online");
        if (onlineDraft.ProfileId == profile.Id || onlineDraft.ModIds.Count != 2) throw new InvalidOperationException("Favorite/profile binding lost.");
    }
}
