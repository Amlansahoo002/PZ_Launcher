namespace PZLauncher.Desktop;

[Flags]
internal enum WindowEdge { Left = 1, Right = 2, Top = 4, Bottom = 8 }



internal sealed class WindowResizeHandle : Control
{
    private readonly Form window;
    internal WindowEdge Edge { get; }
    private bool dragging;
    private Rectangle initialBounds;
    private Point initialPointer;
    internal WindowResizeHandle(Form window, WindowEdge edge)
    {
        this.window = window; Edge = edge;
        Name = "WindowResize" + edge.ToString().Replace(", ", "");
        AccessibleName = T("window.resize"); AccessibleRole = AccessibleRole.Grip;
        BackColor = Theme.Sidebar; TabStop = false; DoubleBuffered = true; ResizeRedraw = true;
        SetStyle(ControlStyles.Selectable, false);
        Cursor = edge switch
        {
            WindowEdge.Left or WindowEdge.Right => Cursors.SizeWE,
            WindowEdge.Top or WindowEdge.Bottom => Cursors.SizeNS,
            WindowEdge.Top | WindowEdge.Left or WindowEdge.Bottom | WindowEdge.Right => Cursors.SizeNWSE,
            _ => Cursors.SizeNESW
        };
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || window.WindowState != FormWindowState.Normal) return;
        initialBounds = window.Bounds; initialPointer = PointToScreen(e.Location);
        dragging = true; Capture = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging || !Capture || window.WindowState != FormWindowState.Normal) return;
        Point pointer = PointToScreen(e.Location);
        window.Bounds = ResizeBounds(initialBounds, new(pointer.X - initialPointer.X, pointer.Y - initialPointer.Y), Edge,
            window.MinimumSize, window.MaximumSize);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        dragging = false; Capture = false;
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) dragging = false;
    }
    internal static Rectangle ResizeBounds(Rectangle start, Point delta, WindowEdge edge, Size minimum, Size maximum)
    {
        bool left = edge.HasFlag(WindowEdge.Left), right = edge.HasFlag(WindowEdge.Right);
        bool top = edge.HasFlag(WindowEdge.Top), bottom = edge.HasFlag(WindowEdge.Bottom);
        int minWidth = Math.Max(1, minimum.Width), minHeight = Math.Max(1, minimum.Height);
        int width = Math.Clamp(start.Width + (left ? -delta.X : right ? delta.X : 0), minWidth,
            maximum.Width > 0 ? Math.Max(minWidth, maximum.Width) : int.MaxValue);
        int height = Math.Clamp(start.Height + (top ? -delta.Y : bottom ? delta.Y : 0), minHeight,
            maximum.Height > 0 ? Math.Max(minHeight, maximum.Height) : int.MaxValue);
        return new(left ? start.Right - width : start.Left, top ? start.Bottom - height : start.Top, width, height);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Edge != (WindowEdge.Bottom | WindowEdge.Right)) return;
        float scale = DeviceDpi / 96f;
        using var pen = new Pen(Theme.Muted, Math.Max(1, scale));
        for (int offset = 5; offset <= 13; offset += 4)
            e.Graphics.DrawLine(pen, Width - offset * scale, Height - 3 * scale, Width - 3 * scale, Height - offset * scale);
    }
}
