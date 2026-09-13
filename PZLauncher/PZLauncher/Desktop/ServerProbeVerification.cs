using System.Text.Json;

namespace PZLauncher.Desktop;

internal static class ServerProbeVerification
{
    internal static void Run(Action<string, Action> check)
    {
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        check("Online : filtres combinés sur joueurs, tags, version exacte et ping", () =>
        {
            var filter = new OnlineServerFilter { MinimumPlayers = 2, MaximumPing = 80, Mods = 1, Version = "42.20" };
            var server = new OnlineServerInfo { Name = "Fixture", Players = 10, Ping = 30, Tags = ";modded;pvp", Version = "42.20" };
            Assert(filter.Matches(server), "Matching server hidden.");
            server.Players = 1; Assert(!filter.Matches(server), "Minimum ignored."); server.Players = 2;
            server.Ping = 81; Assert(!filter.Matches(server), "Ping ignored."); server.Ping = -1;
            Assert(!filter.Matches(server), "Unknown ping included under a cap."); server.Ping = 30;
            server.Version = "42.200"; Assert(!filter.Matches(server), "Version uses unsafe substring matching."); server.Version = "42.20";
            server.Tags = ""; Assert(!filter.Matches(server) && server.HasMods == null, "Unknown tags treated as vanilla.");
            server.Tags = ";vanilla"; filter.Mods = 2; Assert(filter.Matches(server), "Vanilla filter failed.");
            filter.Search = "absent"; Assert(!filter.Matches(server), "Search ignored.");
        });
        check("Online : capture complète distincte des règles publiques, persistée avec horodatages", () =>
        {
            var server = new OnlineServerInfo { Rules = new() { ["mods"] = "First", ["modCount"] = "2" }, FullMods = new()
                { GameVersion = "42.20", CapturedAt = DateTimeOffset.UtcNow, Mods = [new("First", "123", "One"), new("Second", "123", "Two")], Workshop = [new("123", 1760000000)] } };
            server.FullMods.Validate();
            var restored = JsonSerializer.Deserialize<OnlineServerInfo>(JsonSerializer.Serialize(server))!;
            Assert(restored.CompleteModList && restored.ModIds.SequenceEqual(new[] { "First", "Second" }) && restored.Rules["mods"] == "First", "Captured list lost or public rules rewritten.");
            Assert(restored.FullMods!.WorkshopIds.SequenceEqual(new[] { "123" }) && restored.FullMods.Workshop[0].Updated == 1760000000, "Workshop association/timestamps lost.");
            restored.FullMods.Mods.Add(new("First", "123", "duplicate"));
            bool rejected = false; try { restored.FullMods.Validate(); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Duplicate metadata accepted as complete.");
        });
        check("Online : bouton de capture et données complètes dans la fiche", () =>
        {
            using var dialog = new OnlineServerDialog(new() { Name = "Fixture", FullMods = new()
                { GameVersion = "42.20", CapturedAt = DateTimeOffset.UtcNow, Mods = [new("Fixture", "123", "Fixture mod")], Workshop = [new("123", 1760000000)] } }, "", CancellationToken.None, false);
            Assert(dialog.Controls.Find("GrabAllMods", true).Length == 1, "Capture button absent.");
            Assert(!dialog.CapturedThisSession, "Viewing a saved capture must not replace edited favorite lists.");
            string text = dialog.Controls.Find("ServerMods", true).Single().Text;
            Assert(text.Contains("Fixture mod") && text.Contains("123") && text.Contains("1760000000"), "Detailed metadata missing.");
        });
    }
    internal static int Smoke(bool steam = false)
    {
        string output = Path.Combine(LauncherStorage.Root, "server-probe-verification.json");
        try
        {
            string installation = InstallationLocator.Find("");
            var server = new OnlineServerInfo { Host = "127.0.0.1", Port = 28261 };
            var stages = new List<string>();
            var capture = Wait(ServerModProbe.RunAsync(installation, server, new("probe-player", "probe-player-password", "probe-local-only", "", steam, false),
                CancellationToken.None, new Progress<string>(s => stages.Add(s))));
            if (capture.Mods.Count != 2 || capture.Mods.Any(m => !m.Id.StartsWith("PZLauncherProbe_LongIdentifier_"))) throw new Exception("Unexpected actual-server metadata.");
            bool denied = false;
            try { Wait(ServerModProbe.RunAsync(installation, server, new("probe-player", "wrong-password", "probe-local-only", "", steam, false), CancellationToken.None)); }
            catch (IOException ex) { denied = ex.Message.Contains("InvalidUsernamePassword", StringComparison.OrdinalIgnoreCase); }
            if (!denied) throw new Exception("Invalid account password was not refused.");
            using var cancellation = new CancellationTokenSource(1200);
            bool cancelled = false;
            try { Wait(ServerModProbe.RunAsync(installation, new() { Host = "127.0.0.1", Port = 28271 },
                new("probe-player", "", "", "", false, false), cancellation.Token)); }
            catch (OperationCanceledException) { cancelled = true; }
            if (!cancelled) throw new Exception("Cancellation failed.");
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, installation, transport = "real PZ 42.20 dedicated server, loopback, Steam=" + steam, capture, denied, cancelled, stages }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() })); return 1; }
    }
    private static T Wait<T>(Task<T> task)
    {
        while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); }
        return task.GetAwaiter().GetResult();
    }
}
