using System.Diagnostics;
using System.Text.Json;

namespace PZLauncher.Desktop;

internal static class ServerStatisticsVerification
{
    internal static List<OnlineServerInfo> Fixtures()
    {
        var now = DateTimeOffset.Now;
        return
        [
            new() { Host = "192.0.2.1", Responded = true, Players = 7, Version = "42.20.0", Tags = ";modded", CheckedAt = now,
                Rules = new() { ["mods"] = "Alpha;Beta;Alpha", ["modCount"] = "5" } },
            new() { Host = "192.0.2.1", Responded = true, Players = 100, Version = "old", CheckedAt = now.AddMinutes(-1) },
            new() { Host = "192.0.2.2", Responded = true, Players = 0, Version = "41.78.16", Tags = ";vanilla",
                Rules = new() { ["mods"] = "", ["modCount"] = "0" }, FullMods = new() { Mods = [new("PrivateCapture", "", "")] } },
            new() { Host = "192.0.2.3", Responded = true, Players = -1, Version = "", Rules = new() { ["mods"] = "Bad\u0001", ["modCount"] = "-1" } },
            new() { Host = "192.0.2.4", Responded = true, Players = 3, Version = "42.20.0", Tags = ";modded",
                Rules = new() { ["mods"] = "Alpha;Gamma", ["modCount"] = "2" }, FullMods = new() { Mods = [new("PrivateCapture", "", "")] } },
            new() { Host = "192.0.2.5", Responded = false, Players = 999 }
        ];
    }
    internal static void Run(Action<string, Action> check)
    {
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        check("Statistiques : serveurs uniques, vanilla/moddés/inconnus, joueurs et versions", () =>
        {
            var stats = OnlineServerStatistics.Build(Fixtures());
            Assert(stats.Total == 4 && stats.Vanilla == 1 && stats.Modded == 2 && stats.UnknownMods == 1, "Missing data or saved captures skew classification.");
            Assert(stats.Occupied == 2 && stats.Empty == 1 && stats.UnknownPlayers == 1 && stats.Players == 10, "Player count or duplicate response counted incorrectly.");
            Assert(stats.Versions.Single(v => v.Version == "42.20.0") == new ServerVersionCount("42.20.0", 2, 2, 10), "Version/player grouping incorrect.");
            Assert(stats.Versions.Single(v => v.Version == "").Servers == 1, "Unknown version lost.");
        });
        check("Statistiques : top 50 public, doublons par serveur, couverture et départage stable", () =>
        {
            var stats = OnlineServerStatistics.Build(Fixtures());
            Assert(stats.PublicLists == 3 && stats.CompleteLists == 2, "Malformed/truncated lists counted as complete.");
            Assert(stats.TopMods.SequenceEqual(new[] { new VisibleModCount("Alpha", 2), new VisibleModCount("Beta", 1), new VisibleModCount("Gamma", 1) }), "Ranking includes duplicate occurrences or private captures.");
            var many = Enumerable.Range(0, 75).Select(i => new OnlineServerInfo { Host = "192.0.2." + (i + 1), Responded = true,
                Rules = new() { ["mods"] = "Mod" + i.ToString("D2") } }).ToArray();
            stats = OnlineServerStatistics.Build(many);
            Assert(stats.TopMods.Count == 50 && stats.TopMods[0].Id == "Mod00" && stats.TopMods[^1].Id == "Mod49", "Top 50 limit/ties unstable.");
            var empty = OnlineServerStatistics.Build([]);
            Assert(empty.Total == 0 && empty.TopMods.Count == 0 && empty.Versions.Count == 0, "Empty sample invented data.");
        });
        check("Statistiques : collecte progressive annulable, requêtes publiques et réponses conservées", () =>
        {
            var source = Fixtures();
            async Task<SteamBrowserResult> Query(IReadOnlyList<SteamRulesTarget> targets, Action<SteamBrowserProgress> report, CancellationToken token)
            {
                Assert(targets.Count == 3 && targets.All(t => t.Host != "192.0.2.2"), "Vanilla or duplicate targets queried.");
                report(new() { Discovered = targets.Count, Completed = 1, Servers = [new() { Host = "192.0.2.3", Port = 16261,
                    Rules = new() { ["mods"] = "NewPublicMod", ["modCount"] = "20", ["version"] = "41.78.16" } }] });
                await Task.Delay(Timeout.Infinite, token); return new();
            }
            using var dialog = new OnlineStatisticsDialog(source, true, "", CancellationToken.None, Query) { Opacity = 0 };
            dialog.Show(); Application.DoEvents();
            var collect = (ActionButton)dialog.Controls.Find("CollectPublicMods", true).Single();
            collect.PerformClick();
            Assert(!dialog.CollectionTask.IsCompleted && dialog.UpdatedServers.Count == 1, "Progress waited for final result.");
            Assert(source.Single(s => s.Host == "192.0.2.3").Rules["mods"] == "Bad\u0001", "Dialog changed source before being accepted back by browser.");
            collect.PerformClick(); Wait(dialog.CollectionTask);
            Assert(dialog.UpdatedServers.Single().Rules["mods"] == "NewPublicMod", "Cancellation discarded received public data.");
            Assert(dialog.Controls.Find("StatisticsCollectionStatus", true).Single().Text == T("stats.cancelled"), "Cancellation not reported.");
            Assert(((DataGridView)dialog.Controls.Find("StatisticsTopMods", true).Single()).Rows.Count == 4, "Progress did not update ranking.");
            Assert(((DataGridView)dialog.Controls.Find("StatisticsVersions", true).Single()).Rows.Count == 2, "Version returned in public rules remained unknown.");
            collect.PerformClick(); dialog.Close(); Wait(dialog.CollectionTask);
            Assert(dialog.IsDisposed, "Closing an active collection did not cancel and close the dialog.");
        });
    }
    internal static int Smoke()
    {
        string output = Path.Combine(LauncherStorage.Root, "server-statistics-verification.json");
        try
        {
            string installation = InstallationLocator.Find("");
            var list = Wait(SteamServerBrowser.RunAsync(installation, null, CancellationToken.None));
            var targets = list.Servers.Where(s => OnlineServerStatistics.PublicHasMods(s) == true).Take(12)
                .Select(s => new SteamRulesTarget(s.Host, s.Port, s.QueryPort)).ToList();
            if (targets.Count < 4) throw new InvalidOperationException("Not enough actual Steam servers for verification.");
            targets.Add(new("127.0.0.1", 28271, 28271)); 
            int batches = 0, received = 0, failed = 0; long firstMs = -1; var watch = Stopwatch.StartNew();
            var result = Wait(SteamServerBrowser.RunAsync(installation, null, CancellationToken.None, batch =>
            {
                batches++; received += batch.Servers.Count; failed = batch.Failed;
                if (firstMs < 0 && batch.Servers.Count > 0) firstMs = watch.ElapsedMilliseconds;
            }, targets));
            long finishedMs = watch.ElapsedMilliseconds;
            if (received < 1 || received != result.Servers.Count || !result.Partial || failed < 1 || firstMs >= finishedMs - 500)
                throw new InvalidOperationException("Batch progress or partial failure handling failed.");
            var lookup = list.Servers.ToDictionary(s => (s.Host, s.Port));
            foreach (var server in result.Servers) lookup[(server.Host, server.Port)].Rules = server.Rules;
            var stats = OnlineServerStatistics.Build(list.Servers);
            if (stats.TopMods.Count < 1) throw new InvalidOperationException("Real public rules did not yield mod IDs.");
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20)); int kept = 0;
            bool cancelled = false;
            try { Wait(SteamServerBrowser.RunAsync(installation, null, cancel.Token, batch =>
                { kept += batch.Servers.Count; if (kept > 0) cancel.Cancel(); }, targets)); }
            catch (OperationCanceledException) { cancelled = true; }
            if (!cancelled || kept == 0) throw new InvalidOperationException("Cancellation before batch completion was not observed.");
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, installation, queried = targets.Count, received, failed,
                batches, firstMs, finishedMs, cancellationKept = kept, statistics = stats }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() })); return 1; }
    }
    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed.TotalSeconds < 70) { Application.DoEvents(); Thread.Sleep(10); }
        if (!task.IsCompleted) throw new TimeoutException("Verification task did not finish.");
        task.GetAwaiter().GetResult();
    }
}
