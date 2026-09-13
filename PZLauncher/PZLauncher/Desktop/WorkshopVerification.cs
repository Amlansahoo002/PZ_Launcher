using System.Diagnostics;
using System.Text.Json;

namespace PZLauncher.Desktop;

internal static class WorkshopVerification
{
    internal static void Run(string root, Action<string, Action> check)
    {
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Reject(Action action)
        {
            bool rejected = false; try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException) { rejected = true; }
            Assert(rejected, "Operation was unexpectedly accepted.");
        }
        check("SteamCMD : identifiants, liens, doublons et commandes limitées à 108600", () =>
        {
            var ids = WorkshopStore.ParseIds("123; https://steamcommunity.com/sharedfiles/filedetails/?id=456&searchtext=test\n123");
            Assert(ids.SequenceEqual(new[] { "123", "456" }), "Workshop IDs parsed incorrectly.");
            foreach (string bad in new[] { "0", "123 +quit", "https://example.com/?id=123", "18446744073709551616", "https://steamcommunity.com/id/account" })
                Reject(() => WorkshopStore.ParseIds(bad));
            Reject(() => SteamCmdClient.Account("name\nquit"));
            var args = SteamCmdClient.DownloadArguments(ids, "", true);
            Assert(args.Count(a => a == "108600") == 2 && args.Contains("anonymous") && args.Count(a => a == "validate") == 2 && args.Last() == "+quit", "Invalid command arguments.");
            Assert(!args.Contains("+app_update"), "Downloader can update the game instead of Workshop items.");
        });
        check("SteamCMD : messages fragmentés, succès par ID et erreurs malgré un code de sortie nul", () =>
        {
            var tracker = new WorkshopDownloadTracker();
            string stream = "Success. Downloaded item 123 to \"C:/path\" (1 bytes)\r\nERROR! Download item 456 failed (Access Denied).";
            foreach (char c in stream) tracker.Append(c.ToString());
            Assert(tracker.Results.GetValueOrDefault("123", "missing") == "" && tracker.Results["456"] == "Access Denied", "Split output not understood.");
            Assert(!tracker.Results.ContainsKey("12") && !tracker.Results.ContainsKey("789"), "Partial/absent item incorrectly reported successful.");
            tracker.Append("ERROR! Download item 123 failed (No Connection).");
            Assert(tracker.Results["123"] == "No Connection", "Later failure ignored.");
            tracker.Append("Success. Downloaded item 123 to \"C:/path\" (1 bytes)\r\n");
            Assert(tracker.Results["123"] == "", "Later success ignored.");
        });
        string downloads = Path.Combine(root, "workshop-downloads");
        string cache = Path.Combine(root, "workshop-profile");
        string sourceMod = Path.Combine(downloads, "mods", "ExampleMod");
        Directory.CreateDirectory(Path.Combine(sourceMod, "42")); Directory.CreateDirectory(Path.Combine(sourceMod, "common"));
        File.WriteAllText(Path.Combine(sourceMod, "42", "mod.info"), "id=ExampleId\nname=Workshop example\n");
        File.WriteAllText(Path.Combine(sourceMod, "common", "old.txt"), "old-version");
        check("SteamCMD : installation B42, provenance Workshop et chargement local réel", () =>
        {
            var result = WorkshopStore.Install(cache, "123", downloads, CancellationToken.None);
            Assert(result.Mods.Single().ModIds.Single() == "ExampleId", "Mod ID not preserved.");
            var profile = new PlayerProfile { CachePath = cache, Steam = false };
            profile.Launch.ModFolders = WorkshopStore.PreferLocal("workshop,steam,mods");
            var catalog = ModCatalog.Scan(profile, new Version(42, 20));
            var mod = catalog.Single(m => m.Id == "ExampleId");
            Assert(mod.Source == "SteamCMD" && mod.WorkshopId == "123" && mod.Available, "Downloaded mod not discoverable in no-Steam mode.");
            Assert(profile.Launch.ModFolders == "mods,workshop,steam", "Local version does not have priority.");
            Assert(File.ReadAllText(Path.Combine(mod.Location, "common", "old.txt")) == "old-version", "Game-visible files are wrong.");
        });
        check("SteamCMD : mise à jour, suppression des fichiers obsolètes et backup", () =>
        {
            File.Delete(Path.Combine(sourceMod, "common", "old.txt"));
            File.WriteAllText(Path.Combine(sourceMod, "common", "new.txt"), "new-version");
            WorkshopStore.Install(cache, "123", downloads, CancellationToken.None);
            string target = Path.Combine(cache, "mods", "ExampleMod", "common");
            Assert(!File.Exists(Path.Combine(target, "old.txt")) && File.ReadAllText(Path.Combine(target, "new.txt")) == "new-version", "Stale files survived the update.");
            string backup = Directory.EnumerateFiles(Path.Combine(cache, ".pzlauncher-workshop", "backups"), "old.txt", SearchOption.AllDirectories).Single();
            Assert(File.ReadAllText(backup) == "old-version", "Previous version not backed up.");
            Assert(WorkshopStore.Read(cache).Count == 1, "Update duplicated ownership records.");
        });
        check("SteamCMD : conflit avec un dossier non géré et contenu non mod refusés", () =>
        {
            Reject(() => WorkshopStore.Install(cache, "456", downloads, CancellationToken.None));
            Assert(File.ReadAllText(Path.Combine(cache, "mods", "ExampleMod", "common", "new.txt")) == "new-version", "Conflicting item overwrote a mod.");
            string otherCache = Path.Combine(root, "workshop-unmanaged");
            Directory.CreateDirectory(Path.Combine(otherCache, "mods", "ExampleMod"));
            File.WriteAllText(Path.Combine(otherCache, "mods", "ExampleMod", "mine.txt"), "user-file");
            Reject(() => WorkshopStore.Install(otherCache, "123", downloads, CancellationToken.None));
            Assert(File.ReadAllText(Path.Combine(otherCache, "mods", "ExampleMod", "mine.txt")) == "user-file", "Unmanaged folder overwritten.");
            string empty = Path.Combine(root, "not-a-mod"); Directory.CreateDirectory(empty);
            Reject(() => WorkshopStore.Install(otherCache, "456", empty, CancellationToken.None));
        });
        check("SteamCMD : annulation et rollback si la validation finale échoue", () =>
        {
            string index = WorkshopStore.IndexPath(cache), original = File.ReadAllText(index);
            File.WriteAllText(Path.Combine(sourceMod, "common", "new.txt"), "must-not-commit");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Reject(() => WorkshopStore.Install(cache, "123", downloads, cancellation.Token));
            using (var locked = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(() => WorkshopStore.Install(cache, "123", downloads, CancellationToken.None));
            Assert(File.ReadAllText(index) == original, "Ownership index changed after failed commit.");
            Assert(File.ReadAllText(Path.Combine(cache, "mods", "ExampleMod", "common", "new.txt")) == "new-version", "Previous mod was not restored.");
            Assert(!Directory.EnumerateDirectories(Path.Combine(cache, ".pzlauncher-workshop", "staging")).Any(), "Incomplete staging left behind.");
        });
        check("SteamCMD : annulation du groupe de processus Windows", () =>
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command"); start.ArgumentList.Add("Start-Sleep -Seconds 90");
            using var process = Process.Start(start)!;
            using (var job = new WorkshopProcessJob(process)) { }
            Assert(process.WaitForExit(5000), "Job disposal did not stop its child.");
        });
    }
    internal static int Smoke()
    {
        var client = new SteamCmdClient();
        int failures = 0; string error = "";
        try { client.SmokeAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch (Exception ex) { failures = 1; error = ex.ToString(); }
        string path = Path.Combine(LauncherStorage.Root, "steamcmd-verification.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now, failures, error, output = client.Output,
            method = "Valve bootstrap, anonymous login and expected rejection of a nonexistent 108600 Workshop item; no real mod installed." }, new JsonSerializerOptions { WriteIndented = true }));
        return failures;
    }
}
