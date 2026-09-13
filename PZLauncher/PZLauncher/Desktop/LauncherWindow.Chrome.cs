using System.Runtime.InteropServices;

namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    private Rectangle normalWindowBounds;
    private bool lastWindowMaximized;
    private readonly List<WindowResizeHandle> resizeHandles = [];


    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style |= 0x40000 | 0x20000 | 0x10000; 
            return parameters;
        }
    }
    private void InitializeWindowPlacement()
    {
        if (!rendering && state.Window is { } saved)
            RestoreWindowPlacement(saved);
        else normalWindowBounds = Bounds;
        LocationChanged += (_, _) => TrackWindowPlacement();
        SizeChanged += (_, _) => TrackWindowPlacement();
        ClientSizeChanged += (_, _) => ArrangeResizeHandles();
        DpiChanged += (_, _) => ArrangeResizeHandles();
    }
    private void RestoreWindowPlacement(WindowPlacement saved)
    {
        var screen = Screen.FromRectangle(new Rectangle(saved.X, saved.Y, Math.Max(1, saved.Width), Math.Max(1, saved.Height)));
        MinimumSize = new(Math.Min(1080, screen.WorkingArea.Width), Math.Min(700, screen.WorkingArea.Height));
        StartPosition = FormStartPosition.Manual;
        
        _ = Handle;
        Bounds = saved.Fit(screen.WorkingArea, MinimumSize);
        normalWindowBounds = Bounds; lastWindowMaximized = saved.Maximized;
        WindowState = saved.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
    }
    private void TrackWindowPlacement()
    {
        if (WindowState == FormWindowState.Minimized) return;
        lastWindowMaximized = WindowState == FormWindowState.Maximized;
        if (!lastWindowMaximized) normalWindowBounds = Bounds;
    }
    private WindowPlacement CaptureWindowPlacement() => new()
    {
        X = normalWindowBounds.X, Y = normalWindowBounds.Y, Width = normalWindowBounds.Width,
        Height = normalWindowBounds.Height, Maximized = lastWindowMaximized
    };

    private void BuildWindowChrome()
    {
        resizeHandles.Clear();
        var chrome = new Panel { Name = "WindowChrome", Dock = DockStyle.Top, Height = 36, BackColor = Theme.Sidebar, Padding = new Padding(0, 6, 6, 0) };
        var caption = Theme.Label("PZLAUNCHER   /   COMMUNITY EDITION", 8, color: Theme.Muted);
        caption.SetBounds(23, 9, 470, 23); chrome.Controls.Add(caption);
        chrome.Paint += (_, e) =>
        {
            using var rule = new Pen(Theme.Border); e.Graphics.DrawLine(rule, 0, chrome.Height - 1, chrome.Width, chrome.Height - 1);
            using var accent = new SolidBrush(Theme.Accent); e.Graphics.FillRectangle(accent, 0, chrome.Height - 2, LogicalToDeviceUnits(215), 2);
        };
        void DragWindow(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }
        void ToggleMaximize()
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }
        chrome.MouseDown += DragWindow; caption.MouseDown += DragWindow;
        chrome.DoubleClick += (_, _) => ToggleMaximize(); caption.DoubleClick += (_, _) => ToggleMaximize();
        var close = new WindowCaptionButton(this, 2) { Name = "WindowClose" };
        var maximize = new WindowCaptionButton(this, 1) { Name = "WindowMaximize" };
        var minimize = new WindowCaptionButton(this, 0) { Name = "WindowMinimize" };
        close.Click += (_, _) => Close(); maximize.Click += (_, _) => ToggleMaximize(); minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        chrome.Controls.Add(minimize); chrome.Controls.Add(maximize); chrome.Controls.Add(close);
        Controls.Add(chrome);
        foreach (WindowEdge edge in new[] { WindowEdge.Left, WindowEdge.Right, WindowEdge.Top, WindowEdge.Bottom,
            WindowEdge.Left | WindowEdge.Top, WindowEdge.Right | WindowEdge.Top,
            WindowEdge.Left | WindowEdge.Bottom, WindowEdge.Right | WindowEdge.Bottom })
        {
            var handle = new WindowResizeHandle(this, edge);
            resizeHandles.Add(handle); Controls.Add(handle); tips.SetToolTip(handle, T("window.resize"));
        }
        ArrangeResizeHandles();
    }
    private void ArrangeResizeHandles()
    {
        int border = Math.Max(6, (int)Math.Round(6 * DeviceDpi / 96f));
        int corner = Math.Max(18, (int)Math.Round(18 * DeviceDpi / 96f));
        int width = ClientSize.Width, height = ClientSize.Height;
        foreach (var handle in resizeHandles)
        {
            handle.Visible = WindowState == FormWindowState.Normal;
            handle.Bounds = handle.Edge switch
            {
                WindowEdge.Left => new(0, corner, border, Math.Max(0, height - 2 * corner)),
                WindowEdge.Right => new(width - border, corner, border, Math.Max(0, height - 2 * corner)),
                WindowEdge.Top => new(corner, 0, Math.Max(0, width - 2 * corner), border),
                WindowEdge.Bottom => new(corner, height - border, Math.Max(0, width - 2 * corner), border),
                WindowEdge.Left | WindowEdge.Top => new(0, 0, corner, corner),
                WindowEdge.Right | WindowEdge.Top => new(width - corner, 0, corner, corner),
                WindowEdge.Left | WindowEdge.Bottom => new(0, height - corner, corner, corner),
                _ => new(width - corner, height - corner, corner, corner)
            };
            handle.BringToFront();
        }
    }
    protected override void WndProc(ref Message m)
    {
        
        
        if (m.Msg == 0x24) 
        {
            base.WndProc(ref m);
            var screen = Screen.FromHandle(Handle);
            var info = Marshal.PtrToStructure<MinMaxInfo>(m.LParam);
            info.MaxPosition = new(screen.WorkingArea.Left - screen.Bounds.Left, screen.WorkingArea.Top - screen.Bounds.Top);
            info.MaxSize = screen.WorkingArea.Size;
            Marshal.StructureToPtr(info, m.LParam, false); return;
        }
        base.WndProc(ref m);
        if (m.Msg is 0x85 or 0x86) 
        {
            nint dc = GetWindowDC(Handle);
            if (dc != 0) { try { PaintSizingFrame(dc); } finally { ReleaseDC(Handle, dc); } }
        }
        else if (m.Msg == 0x317 && (m.LParam.ToInt64() & 2) != 0) PaintSizingFrame(m.WParam); 
    }
    private void PaintSizingFrame(nint dc)
    {
        using var graphics = Graphics.FromHdc(dc);
        var origin = PointToScreen(Point.Empty);
        graphics.ExcludeClip(new Rectangle(origin.X - Left, origin.Y - Top, ClientSize.Width, ClientSize.Height));
        using var brush = new SolidBrush(Theme.Sidebar); graphics.FillRectangle(brush, new Rectangle(Point.Empty, Size));
        using var pen = new Pen(Theme.Border); graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        internal Point Reserved;
        internal Size MaxSize;
        internal Point MaxPosition;
        internal Size MinTrackSize;
        internal Size MaxTrackSize;
    }
}
