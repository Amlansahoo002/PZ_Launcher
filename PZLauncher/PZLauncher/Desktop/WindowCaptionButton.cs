namespace PZLauncher.Desktop;

internal sealed class WindowCaptionButton : Button
{
    private readonly Form owner;
    private readonly int kind;
    private bool hover, down;
    internal WindowCaptionButton(Form owner, int kind)
    {
        this.owner = owner; this.kind = kind;
        Width = 46; Dock = DockStyle.Right; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        owner.Resize += OwnerResize; OwnerResize(null, EventArgs.Empty);
        MouseEnter += (_, _) => { hover = true; Invalidate(); };
        MouseLeave += (_, _) => { hover = down = false; Invalidate(); };
        MouseDown += (_, _) => { down = true; Invalidate(); };
        MouseUp += (_, _) => { down = false; Invalidate(); };
    }
    private void OwnerResize(object? sender, EventArgs e)
    {
        AccessibleName = T(kind == 0 ? "window.minimize" : kind == 2 ? "window.close" : owner.WindowState == FormWindowState.Maximized ? "window.restore" : "window.maximize");
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(hover || down ? kind == 2 ? Color.FromArgb(down ? 170 : 205, 42, 48) : Theme.Hover : Theme.Sidebar);
        float scale = DeviceDpi / 96f;
        using var pen = new Pen(Theme.Text, Math.Max(1, scale));
        float size = 10 * scale, x = (Width - size) / 2f, y = (Height - size) / 2f;
        if (kind == 0) e.Graphics.DrawLine(pen, x, y + size / 2, x + size, y + size / 2);
        else if (kind == 2) { e.Graphics.DrawLine(pen, x, y, x + size, y + size); e.Graphics.DrawLine(pen, x + size, y, x, y + size); }
        else if (owner.WindowState != FormWindowState.Maximized) e.Graphics.DrawRectangle(pen, x, y, size, size);
        else
        {
            e.Graphics.DrawLines(pen, new PointF[] { new(x + 3 * scale, y), new(x + size, y), new(x + size, y + size - 3 * scale) });
            e.Graphics.DrawRectangle(pen, x, y + 3 * scale, size - 3 * scale, size - 3 * scale);
        }
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
    }
    protected override void Dispose(bool disposing) { if (disposing) owner.Resize -= OwnerResize; base.Dispose(disposing); }
}
