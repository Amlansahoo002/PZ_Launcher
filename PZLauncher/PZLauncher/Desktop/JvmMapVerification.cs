using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using PZ_ChunkWiper;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class JvmMapVerification
{
    internal static void Run(string root, Action<string, Action> check)
    {
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        check("JVM : CPU/GPU, réserve native, valeurs explicites et choix existants", () =>
        {
            var desktop = new HardwareSnapshot(32768, 12288, 24, "Desktop", "25.0.1", "ZGC") { Cores = 12, JavaStackKb = 2048, Gpus = [new("Discrete", 12288, 16384, false)] };
            var unified = desktop with { Gpus = [new("Integrated", 128, 16384, true)] };
            var suggested = HardwareAdvisor.Suggest(desktop);
            Assert(suggested.Collector == "ZGC" && suggested.StackKb == 2048 && suggested.PauseTargetMs == 200, "Suggestion CPU/Java incorrecte.");
            Assert(HardwareAdvisor.Suggest(unified).XmxMb < suggested.XmxMb, "GPU intégré sans réserve supplémentaire.");
            var low = HardwareAdvisor.Suggest(new(8192, 4096, 4, "Small", "25.0.1", "ZGC") { Cores = 2 });
            Assert(low.Collector == "G1" && low.PauseTargetMs == 200 && low.XmsMb <= low.XmxMb, "Petit CPU sans adaptation.");
            var profile = new PlayerProfile(); Assert(HardwareAdvisor.FillUnset(profile, suggested), "Profil vide non initialisé.");
            Assert(profile.MemoryMb > 0 && profile.InitialMemoryMb > 0 && profile.StackKb > 0 && profile.PauseTargetMs > 0 && profile.Collector != "Jeu", "Valeur JVM par défaut laissée à zéro.");
            profile.MemoryMb = 4096; profile.InitialMemoryMb = 512; profile.StackKb = 4096; profile.PauseTargetMs = 300; profile.Collector = "G1";
            Assert(!HardwareAdvisor.FillUnset(profile, suggested) && profile.StackKb == 4096 && profile.MemoryMb == 4096, "Choix explicites écrasés au démarrage.");
            using var window = new LauncherWindow(); window.VerifyJvmAdvice(suggested, root);
            var fallback = HardwareAdvisor.Suggest(new(0, 0, 2, "", "", ""));
            Assert(fallback.XmxMb > 0 && fallback.XmsMb > 0 && fallback.StackKb > 0 && fallback.PauseTargetMs > 0, "Repli matériel nul.");
        });
        check("JVM réelle : Xss explicite, G1/ZGC et cible de pause conditionnelle", () =>
        {
            string install = InstallationLocator.Find(""); var live = HardwareAdvisor.Detect(install); var suggestion = HardwareAdvisor.Suggest(live);
            Assert(live.Cores > 0 && live.Gpus.Count > 0 && live.JavaStackKb >= 1024, "Inventaire local CPU/GPU/pile incomplet.");
            File.WriteAllText(Path.Combine(root, "hardware.json"), JsonSerializer.Serialize(new { hardware = live, suggestion }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (string collector in new[] { "G1", "ZGC" })
            {
                var profile = new PlayerProfile { CachePath = root, Collector = collector, MemoryMb = suggestion.XmxMb, InitialMemoryMb = 64, StackKb = suggestion.StackKb, PauseTargetMs = suggestion.PauseTargetMs, StringDeduplication = true };
                var plan = new GameLaunchService().CreateLaunchPlan(install, profile.AsGameProfile(), profile.AsJvmProfile());
                var start = new ProcessStartInfo(plan.JavaExecutable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
                foreach (string argument in plan.Arguments.TakeWhile(a => a != "-cp")) start.ArgumentList.Add(argument);
                start.ArgumentList.Add("-XX:+PrintFlagsFinal"); start.ArgumentList.Add("-version");
                using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10000)) { process.Kill(); throw new IOException("JVM probe timeout"); }
                string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
                Assert(process.ExitCode == 0, output);
                Assert(System.Text.RegularExpressions.Regex.IsMatch(output, @"\bThreadStackSize\s*=\s*" + suggestion.StackKb + @"\s"), "Xss ignoré par la JVM.");
                Assert(plan.Arguments.Any(a => a.StartsWith("-XX:MaxGCPauseMillis=")) == (collector == "G1"), "Pause appliquée au mauvais collecteur.");
            }
        });
        check("Carte : GIF animé conservé, source libérée et image précédente gardée sur erreur", () =>
        {
            
            string path = Path.Combine(root, "two-frames.gif");
            File.WriteAllBytes(path, Convert.FromHexString("4749463839610100010080000000000000FF0021F904040A0000002C000000000100010000020244010021F904040A0000002C00000000010001000002024C01003B"));
            using var viewer = new MapViewerControl { Size = new Size(500, 400) }; viewer.LoadBackground(path);
            Assert(ImageAnimator.CanAnimate(viewer.BackgroundImage) && viewer.BackgroundImage.GetFrameCount(FrameDimension.Time) == 2, "GIF aplati ou perdu.");
            File.Move(path, path + ".moved"); Assert(File.Exists(path + ".moved"), "Le GIF source reste verrouillé.");
            File.WriteAllText(path, "invalid image"); bool failed = false;
            try { viewer.LoadBackground(path); } catch (ArgumentException) { failed = true; }
            Assert(failed && viewer.BackgroundImage.GetFrameCount(FrameDimension.Time) == 2, "Image valide perdue après erreur de chargement.");
            using var bitmap = new Bitmap(500, 400); viewer.DrawToBitmap(bitmap, new Rectangle(0, 0, 500, 400));
        });
        check("Carte : images 1:1 et 1:4 alignées, sélection stable et échelle fractionnaire", () =>
        {
            string full = Path.Combine(root, "map-full.png"), reduced = Path.Combine(root, "map-quarter.png");
            using (var bitmap = new Bitmap(1024, 1024))
            {
                using var g = Graphics.FromImage(bitmap); g.Clear(Color.Black);
                g.FillRectangle(Brushes.Red, 256, 256, 256, 256); bitmap.Save(full, ImageFormat.Png);
            }
            using (var bitmap = new Bitmap(256, 256))
            {
                using var g = Graphics.FromImage(bitmap); g.Clear(Color.Black);
                g.FillRectangle(Brushes.Red, 64, 64, 64, 64); bitmap.Save(reduced, ImageFormat.Png);
            }
            using var viewer = new MapViewerControl { Size = new Size(300, 300), ShowGridCell = false, ShowCellCoords = false,
                BackgroundAlpha = 1, SelectedCellRect = new Rectangle(1, 1, 1, 1) };
            using var output = new Bitmap(300, 300);
            foreach (var (file, scale) in new[] { (full, 1f), (reduced, 4f) })
            {
                viewer.LoadBackground(file); viewer.BackgroundTilesPerPixel = scale;
                Assert(viewer.BackgroundBounds == new RectangleF(0, 0, 256, 256), "Image/calibration incorrectly maps world extent.");
                viewer.DrawToBitmap(output, new Rectangle(0, 0, 300, 300));
                Assert(output.GetPixel(80, 80).ToArgb() == Color.Red.ToArgb() && output.GetPixel(40, 40).ToArgb() == Color.Black.ToArgb(), "Known image cell is misaligned.");
                Assert(viewer.ScreenToCell(new Point(80, 80)) == new Point(1, 1) && viewer.ScreenToCell(new Point(-1, -1)) == new Point(-1, -1), "Selection coordinates changed with image calibration.");
            }
            viewer.BackgroundTilesPerPixel = 1.5f;
            Assert(viewer.BackgroundBounds.Width == 96 && viewer.SelectedCellRect == new Rectangle(1, 1, 1, 1), "Fractional scale rounded or selection changed.");
            bool rejected = false; try { viewer.BackgroundTilesPerPixel = 0; } catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert(rejected && viewer.BackgroundTilesPerPixel == 1.5f, "Invalid scale was accepted.");
        });
        check("Carte : calibration mémorisée par image et contrôles synchronisés", () =>
        {
            var settings = new MapPreviewSettings();
            string full = Path.Combine(root, "map-full.png"), reduced = Path.Combine(root, "map-quarter.png");
            settings.Remember(full, 1); settings.Remember(reduced, 4);
            var loaded = JsonSerializer.Deserialize<MapPreviewSettings>(JsonSerializer.Serialize(settings))!;
            Assert(loaded.ImagePath == reduced && loaded.ScaleFor(full.ToUpperInvariant()) == 1 && loaded.ScaleFor(reduced) == 4
                && loaded.ScaleFor(Path.Combine(root, "new.png")) == 1, "Per-image calibration was not restored.");
            using var window = new LauncherWindow(verification: true); window.VerifyMapCalibration(full);
        });
        check("Carte : détacher/réintégrer, conserver la sélection et fermer avec la page", () =>
        {
            using var owner = new Form(); var embedded = new Panel { Dock = DockStyle.Fill }; owner.Controls.Add(embedded);
            var content = new Panel { Dock = DockStyle.Fill }; embedded.Controls.Add(content);
            var viewer = new MapViewerControl { Dock = DockStyle.Fill, SelectedCellRect = new Rectangle(3, 4, 2, 3) }; content.Controls.Add(viewer);
            bool busy = false; using var host = new MapWindowHost(owner, embedded, content, "Viewer verification", () => busy);
            host.Detach(); Assert(host.Window != null && content.Parent == host.Window, "Fenêtre détachée absente.");
            busy = true; host.Window!.Close(); Assert(host.Window != null, "Fenêtre fermée pendant une opération.");
            busy = false; host.Window!.Close();
            Assert(host.Window == null && content.Parent == embedded && !viewer.IsDisposed && viewer.SelectedCellRect == new Rectangle(3, 4, 2, 3), "État perdu à la réintégration.");
            host.Detach(); var window = host.Window!; embedded.Dispose();
            Assert(window.IsDisposed && viewer.IsDisposed, "Fenêtre orpheline après fermeture de page.");
        });
        string localMap = Path.Combine(AppContext.BaseDirectory, "world.gif");
        if (!File.Exists(localMap)) localMap = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "dist", "world.gif"));
        if (File.Exists(localMap)) check("Carte locale : chargement du grand world.gif dans le visualiseur", () =>
        {
            using var viewer = new MapViewerControl { Size = new Size(800, 500) };
            viewer.LoadBackground(localMap);
            Assert(viewer.BackgroundImage.Width > 0 && viewer.BackgroundImage.Height > 0, "Fond local vide.");
            using var bitmap = new Bitmap(800, 500); viewer.DrawToBitmap(bitmap, new Rectangle(0, 0, 800, 500));
            File.WriteAllText(Path.Combine(root, "local-map.json"), JsonSerializer.Serialize(new { path = localMap, viewer.BackgroundImage.Width, viewer.BackgroundImage.Height, bytes = new FileInfo(localMap).Length }));
        });
    }
}

internal sealed partial class LauncherWindow
{
    internal void VerifyJvmAdvice(JvmSuggestion suggestion, string root)
    {
        profile = new PlayerProfile { Name = "Advice fixture", CachePath = root, Collector = "G1", MemoryMb = 4096, InitialMemoryMb = 512, StackKb = 4096, PauseTargetMs = 300, GcLogging = true };
        ShowPage("Réglages"); recommendationApply!(suggestion);
        var draft = new PlayerProfile(); foreach (var reader in preferenceReaders) reader(draft);
        if (memoryEditor!.Value != suggestion.XmxMb || draft.InitialMemoryMb != suggestion.XmsMb || draft.StackKb != suggestion.StackKb ||
            draft.PauseTargetMs != suggestion.PauseTargetMs || collectorEditor!.SelectedItem?.ToString() != suggestion.Collector || !draft.GcLogging)
            throw new InvalidOperationException("L'application de la suggestion ne couvre pas l'ensemble des réglages JVM.");
    }
}
