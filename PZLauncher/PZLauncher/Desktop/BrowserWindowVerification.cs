using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PZLauncher.Desktop;

internal static class BrowserWindowVerification
{
    internal static void Run(Action<string, Action> check)
    {
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        check("Online : flux IPC fragmenté UTF-8, progression avant fermeture et annulation", () => Task.Run(async () =>
        {
            string name = "pzlauncher-test-" + Guid.NewGuid().ToString("N");
            using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var received = new TaskCompletionSource<SteamBrowserProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reading = SteamServerBrowser.ReadProgressAsync(pipe, batch => received.TrySetResult(batch), cancellation.Token);
            using var writer = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
            await writer.ConnectAsync(cancellation.Token);
            string json = JsonSerializer.Serialize(new SteamBrowserProgress { Servers = [new() { Name = "Été — Сервер", Host = "192.0.2.1" }], Discovered = 15 });
            byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
            await writer.WriteAsync(bytes.AsMemory(0, bytes.Length / 2), cancellation.Token); await writer.FlushAsync(cancellation.Token);
            Assert(!received.Task.IsCompleted, "Partial JSON was published.");
            await writer.WriteAsync(bytes.AsMemory(bytes.Length / 2), cancellation.Token); await writer.FlushAsync(cancellation.Token);
            var batch = await received.Task.WaitAsync(cancellation.Token);
            Assert(batch.Servers.Single().Name == "Été — Сервер" && batch.Discovered == 15 && !reading.IsCompleted, "Progress waited for helper exit or lost UTF-8.");
            cancellation.Cancel();
            bool cancelled = false; try { await reading; } catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "A connected silent helper could not be cancelled.");
        }).GetAwaiter().GetResult());
        check("Fenêtre : position persistée, écran débranché et dimensions bornées", () =>
        {
            var saved = new WindowPlacement { X = -1500, Y = 90, Width = 1350, Height = 880, Maximized = true };
            saved = JsonSerializer.Deserialize<LauncherState>(JsonSerializer.Serialize(new LauncherState { Window = saved }))!.Window!;
            Assert(saved.Maximized && saved.Fit(new(-1920, 0, 1920, 1080), new(1080, 700)) == new Rectangle(-1500, 90, 1350, 880), "Secondary monitor placement lost.");
            var fit = saved.Fit(new(0, 0, 1280, 720), new(1080, 700));
            Assert(fit == new Rectangle(0, 0, 1280, 720), "Disconnected display leaves the window off-screen.");
            saved.Width = -1; saved.Height = int.MaxValue;
            Assert(saved.Fit(new(0, 0, 1024, 600), new(1080, 700)).Size == new Size(1024, 600), "Corrupted geometry/small screen not bounded.");
        });
        check("Fenêtre : huit poignées natives, taille normale conservée après maximisation", () =>
        {
            using var window = new LauncherWindow(verification: true);
            window.VerifyResizeBehavior();
        });
        check("Fenêtre : glisser les huit zones de prise, limites et taille restaurée", () =>
        {
            using var window = new LauncherWindow(verification: true);
            window.VerifyResizeHandles();
        });
        check("Online : fiche de détails, ports distincts et liste de mods incomplète", () =>
        {
            using var dialog = new OnlineServerDialog(new() { Name = "Fixture", Host = "192.0.2.1", Port = 16261, QueryPort = 27016,
                Map = "Muldraugh, KY", Rules = new() { ["description"] = "Hello <script>", ["mods"] = "One;Two", ["modCount"] = "3" } }, "", CancellationToken.None, false);
            var summary = dialog.Controls.Find("ServerSummary", true).Single().Text;
            Assert(dialog.FormBorderStyle == FormBorderStyle.Sizable && dialog.SizeGripStyle == SizeGripStyle.Show, "Details window lacks a visible resizing grip.");
            Assert(summary.Contains("16261") && summary.Contains("27016") && summary.Contains("Muldraugh"), "Detailed connection data missing.");
            Assert(dialog.Controls.Find("ServerMods", true).Single().Text.StartsWith(T("online.partialList", 2, "3")), "Truncated mods presented as complete.");
            Assert(dialog.Controls.Find("ServerDescription", true).Single().Text == "Hello <script>", "Description interpreted as markup.");
        });
    }
    internal static int Smoke()
    {
        string output = Path.Combine(LauncherStorage.Root, "browser-stream-verification.json");
        try
        {
            string installation = InstallationLocator.Find("");
            var watch = Stopwatch.StartNew(); long firstBatchMs = -1; int batches = 0, received = 0;
            var task = SteamServerBrowser.RunAsync(installation, null, CancellationToken.None, batch =>
            {
                batches++; received += batch.Servers.Count;
                if (firstBatchMs < 0 && batch.Servers.Count > 0) firstBatchMs = watch.ElapsedMilliseconds;
            });
            var result = Wait(task); long finishedMs = watch.ElapsedMilliseconds;
            if (received == 0 || firstBatchMs >= finishedMs - 500 || received != result.Servers.Count)
                throw new InvalidOperationException($"No progressive results: received={received}, final={result.Servers.Count}, first={firstBatchMs}, end={finishedMs}.");
            var target = result.Servers.First(s => s.Players > 0);
            var details = Wait(SteamServerBrowser.RunAsync(installation, new() { Host = target.Host, QueryPort = target.QueryPort }, CancellationToken.None));
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20)); int kept = 0;
            bool cancelled = false;
            try { Wait(SteamServerBrowser.RunAsync(installation, null, cancel.Token, batch => { kept += batch.Servers.Count; if (kept > 0) cancel.Cancel(); })); }
            catch (OperationCanceledException) { cancelled = true; }
            if (!cancelled || kept == 0) throw new InvalidOperationException("No results retained before cancellation.");
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, installation, batches, firstBatchMs, finishedMs,
                received, result.Partial, detailsHost = target.Host, rules = details.Servers.Single().Rules.Count, detailsPartial = details.Partial,
                cancellationKept = kept }, new JsonSerializerOptions { WriteIndented = true }));
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

internal sealed partial class LauncherWindow
{
    internal void VerifyResizeHandles()
    {
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        nint Position(Point point) => (nint)((point.X & 0xffff) | ((point.Y & 0xffff) << 16));
        
        Opacity = 0.01; ShowInTaskbar = false; Show(); Enabled = true; Application.DoEvents();
        void Drag(WindowResizeHandle handle, Point delta)
        {
            var start = new Point(handle.Width / 2, handle.Height / 2);
            var screen = handle.PointToScreen(start);
            _ = handle.Handle;
            var child = GetChildAtPoint(PointToClient(screen));
            Assert(child == handle, $"Content masks resize handle {handle.Edge}: bounds={handle.Bounds}, visible={handle.Visible}, disposed={handle.IsDisposed}, child={child?.GetType().Name}/{child?.Name}/{child?.Bounds}, client={ClientSize}, point={PointToClient(screen)}.");
            SendMessage(handle.Handle, 0x201, 1, Position(start)); 
            Assert(handle.Capture, "Resize handle did not capture the pointer.");
            foreach (var step in new[] { new Point(delta.X / 2, delta.Y / 2), delta })
            {
                var pointer = handle.PointToClient(new(screen.X + step.X, screen.Y + step.Y));
                SendMessage(handle.Handle, 0x200, 1, Position(pointer)); 
            }
            SendMessage(handle.Handle, 0x202, 0, Position(handle.PointToClient(new(screen.X + delta.X, screen.Y + delta.Y))));
            Assert(!handle.Capture, "Pointer capture was not released.");
        }
        foreach (var handle in resizeHandles)
        {
            Bounds = new(80, 80, 1190, 760); PerformLayout();
            Rectangle start = Bounds;
            bool left = handle.Edge.HasFlag(WindowEdge.Left), right = handle.Edge.HasFlag(WindowEdge.Right);
            bool top = handle.Edge.HasFlag(WindowEdge.Top), bottom = handle.Edge.HasFlag(WindowEdge.Bottom);
            Drag(handle, new(left ? -44 : right ? 44 : 0, top ? -32 : bottom ? 32 : 0));
            var expected = new Rectangle(left ? start.Left - 44 : start.Left, top ? start.Top - 32 : start.Top,
                start.Width + (left || right ? 44 : 0), start.Height + (top || bottom ? 32 : 0));
            Assert(Bounds == expected, "Mouse resize failed for " + handle.Edge + ": " + Bounds + ", expected " + expected);
            Drag(handle, new(left ? 44 : right ? -44 : 0, top ? 32 : bottom ? -32 : 0));
            Assert(Bounds == start, "Shrinking moved the opposite edge for " + handle.Edge);
        }
        var oldHandles = resizeHandles.ToArray();
        string language = L.Code;
        ApplyLanguage(language == "fr" ? "en" : "fr", false); ApplyLanguage(language, false);
        Assert(oldHandles.All(h => h.IsDisposed) && resizeHandles.Count == 8 && Controls.OfType<WindowResizeHandle>().Count() == 8,
            "Changing language left stale or duplicate resize handles.");
        ClientSize = new(1080, 700);
        var corner = resizeHandles.Single(h => h.Edge == (WindowEdge.Right | WindowEdge.Bottom));
        Assert(corner.Right == ClientSize.Width && corner.Bottom == ClientSize.Height, "Grip did not follow client size changes.");
        using (var bitmap = new Bitmap(corner.Width, corner.Height))
        {
            corner.DrawToBitmap(bitmap, new(Point.Empty, bitmap.Size));
            Assert(bitmap.GetPixel(corner.Width - 5, corner.Height - 3).ToArgb() == Theme.Muted.ToArgb(), "Resize grip is not painted.");
        }
        Drag(corner, new(-2000, -2000));
        Assert(Size == MinimumSize, "Dragging bypassed minimum size.");
        Drag(corner, new(120, 80));
        WindowPlacement saved = CaptureWindowPlacement();
        WindowState = FormWindowState.Maximized; Application.DoEvents();
        Assert(resizeHandles.All(h => !h.Visible), "Resize handles remain over maximized controls.");
        WindowState = FormWindowState.Normal; Application.DoEvents();
        Assert(resizeHandles.All(h => h.Visible) && Width == saved.Width && Height == saved.Height, "Restore lost dragged size or handles.");
        using var reopened = new LauncherWindow(verification: true) { Opacity = 0, ShowInTaskbar = false };
        reopened.RestoreWindowPlacement(saved); reopened.Show(); Application.DoEvents();
        Rectangle expectedReopen = saved.Fit(Screen.FromRectangle(new(saved.X, saved.Y, saved.Width, saved.Height)).WorkingArea, reopened.MinimumSize);
        Assert(reopened.Bounds == expectedReopen, "A subsequent launch did not retain the dragged dimensions.");
        var maximizeButton = Descendants(this).OfType<WindowCaptionButton>().Single(b => b.Name == "WindowMaximize");
        var minimizeButton = Descendants(this).OfType<WindowCaptionButton>().Single(b => b.Name == "WindowMinimize");
        maximizeButton.PerformClick(); Application.DoEvents(); Assert(WindowState == FormWindowState.Maximized && maximizeButton.AccessibleName == T("window.restore"), "Maximize button failed");
        maximizeButton.PerformClick(); Application.DoEvents(); Assert(WindowState == FormWindowState.Normal && Width == saved.Width, "Restore button lost size");
        minimizeButton.PerformClick(); Application.DoEvents(); Assert(WindowState == FormWindowState.Minimized, "Minimize button failed");
        WindowState = FormWindowState.Normal; Application.DoEvents();
        reopened.Enabled = true;
        Descendants(reopened).OfType<WindowCaptionButton>().Single(b => b.Name == "WindowClose").PerformClick();
        Assert(reopened.IsDisposed, "Close button failed");
    }
    internal void VerifyResizeBehavior()
    {
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        Opacity = 0; ShowInTaskbar = false; Show();
        Enabled = true; Bounds = new Rectangle(50, 50, 1190, 760); PerformLayout();
        int width = Width, height = Height;
        foreach (var (point, expected) in new (Point, int)[] { (new(1, 1), 13), (new(width - 2, 1), 14),
            (new(1, height - 2), 16), (new(width - 2, height - 2), 17), (new(1, height / 2), 10),
            (new(width - 2, height / 2), 11), (new(width / 2, 1), 12), (new(width / 2, height - 2), 15) })
        {
            var screen = new Point(Left + point.X, Top + point.Y);
            Assert(GetChildAtPoint(PointToClient(screen)) == null, "A child window masks the resize border.");
            nint packed = (nint)((screen.X & 0xffff) | ((screen.Y & 0xffff) << 16));
            Assert((int)SendMessage(Handle, 0x84, 0, packed) == expected, "Native edge/corner hit test failed: " + expected);
        }
        Assert((CreateParams.Style & 0x40000) != 0, "Window lacks WS_THICKFRAME.");
        var before = CaptureWindowPlacement();
        WindowState = FormWindowState.Maximized; Application.DoEvents();
        var maximized = CaptureWindowPlacement();
        Assert(maximized.Maximized && maximized.Width == before.Width && maximized.Height == before.Height,
            "Maximizing overwrote normal size: " + System.Text.Json.JsonSerializer.Serialize(new { before, maximized, WindowState }));
        WindowState = FormWindowState.Minimized; Application.DoEvents();
        Assert(CaptureWindowPlacement().Maximized, "Minimizing forgot maximized state.");
        WindowState = FormWindowState.Normal; Application.DoEvents();
        Assert(!CaptureWindowPlacement().Maximized && Size == new Size(before.Width, before.Height),
            "Restoring lost normal dimensions: " + System.Text.Json.JsonSerializer.Serialize(new { before, after = CaptureWindowPlacement(), Size, WindowState, RestoreBounds }));
        
        using var reopened = new LauncherWindow(verification: true) { Opacity = 0, ShowInTaskbar = false };
        reopened.RestoreWindowPlacement(maximized); reopened.Show(); Application.DoEvents();
        Assert(reopened.WindowState == FormWindowState.Maximized, "Reopened window did not restore maximized state.");
        reopened.WindowState = FormWindowState.Normal; Application.DoEvents();
        var expectedBounds = maximized.Fit(Screen.FromRectangle(new(maximized.X, maximized.Y, maximized.Width, maximized.Height)).WorkingArea, reopened.MinimumSize);
        Assert(reopened.Bounds == expectedBounds, "Reopened window did not restore its saved normal bounds: " +
            System.Text.Json.JsonSerializer.Serialize(new { expectedBounds, reopened.Bounds, reopened.ClientSize, reopened.RestoreBounds }));
    }
}
