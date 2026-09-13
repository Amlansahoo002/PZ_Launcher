using System.Drawing.Drawing2D;

namespace PZLauncher.Desktop;



internal static class Atmosphere
{
    internal static void Rain(Graphics graphics, Rectangle bounds, int opacity = 18)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var rain = new Pen(Color.FromArgb(opacity, 162, 196, 205));
        for (int i = 0; i < Math.Min(140, bounds.Width / 7); i++)
        {
            int x = bounds.Left + (i * 137 + 31) % bounds.Width;
            int y = bounds.Top + (i * 71 + 17) % bounds.Height;
            int length = 9 + i * 7 % 27;
            graphics.DrawLine(rain, x, y, x - length / 2f, y + length);
        }
    }

    internal static void Grain(Graphics graphics, Rectangle bounds, int opacity = 12)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var light = new SolidBrush(Color.FromArgb(opacity, 196, 214, 210));
        for (int i = 0; i < Math.Min(900, bounds.Width * bounds.Height / 290); i++)
            graphics.FillRectangle(light, bounds.Left + (i * 173 + 17) % bounds.Width,
                bounds.Top + (i * 109 + i * i % 137) % bounds.Height, 1, 1);
    }

    internal static void Ink(Graphics graphics, RectangleF area, int opacity = 255)
    {
        var saved = graphics.Save();
        graphics.TranslateTransform(area.X, area.Y);
        graphics.ScaleTransform(area.Width / 160f, area.Height / 64f);
        using var ink = new SolidBrush(Color.FromArgb(opacity, Theme.Accent));
        graphics.FillPolygon(ink, new PointF[]
        {
            new(8, 22), new(29, 16), new(26, 8), new(45, 13), new(61, 5), new(64, 14),
            new(101, 9), new(115, 18), new(143, 14), new(136, 29), new(153, 32),
            new(139, 41), new(143, 54), new(114, 50), new(99, 60), new(75, 51),
            new(65, 59), new(40, 49), new(13, 55), new(20, 40), new(3, 36)
        });
        graphics.FillEllipse(ink, 2, 4, 5, 7); graphics.FillEllipse(ink, 149, 52, 7, 6);
        graphics.FillEllipse(ink, 120, 1, 4, 4); graphics.FillEllipse(ink, 0, 49, 4, 3);
        graphics.Restore(saved);
    }

    internal static void Skyline(Graphics graphics, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var silhouette = new SolidBrush(Color.FromArgb(180, 5, 15, 21));
        float unit = bounds.Width / 7f;
        for (int i = 0; i < 8; i++)
        {
            float x = bounds.X + i * unit - 25, roof = bounds.Bottom - bounds.Height * (.22f + i % 3 * .09f);
            graphics.FillPolygon(silhouette, new PointF[] { new(x, bounds.Bottom), new(x, roof),
                new(x + unit * .35f, roof - unit * .20f), new(x + unit * .75f, roof),
                new(x + unit * .75f, bounds.Bottom) });
        }
        using var tree = new Pen(Color.FromArgb(150, 5, 15, 21), Math.Max(1, bounds.Width / 240f));
        float trunk = bounds.Right - bounds.Width * .15f, ground = bounds.Bottom;
        graphics.DrawLines(tree, new PointF[] { new(trunk, ground), new(trunk - 8, ground - bounds.Height * .42f), new(trunk + 5, ground - bounds.Height * .8f) });
        graphics.DrawLines(tree, new PointF[] { new(trunk - 5, ground - bounds.Height * .3f), new(trunk - 34, ground - bounds.Height * .54f), new(trunk - 68, ground - bounds.Height * .61f) });
        graphics.DrawLines(tree, new PointF[] { new(trunk - 4, ground - bounds.Height * .53f), new(trunk + 28, ground - bounds.Height * .69f), new(trunk + 49, ground - bounds.Height * .73f) });
    }
}

internal enum AtmosphereStyle { Sidebar, Masthead, Footer }

internal sealed class AtmosphericPanel : Panel
{
    internal AtmosphereStyle Style;
    internal AtmosphericPanel(AtmosphereStyle style)
    {
        Style = style; DoubleBuffered = true; ResizeRedraw = true;
        BackColor = style == AtmosphereStyle.Masthead ? Theme.Background : Theme.Sidebar;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        if (Width < 1 || Height < 1) return;
        if (Style == AtmosphereStyle.Sidebar)
        {
            using var glow = new LinearGradientBrush(ClientRectangle, Color.FromArgb(25, 45, 55), Theme.Sidebar, 90);
            g.FillRectangle(glow, ClientRectangle); Atmosphere.Rain(g, new(0, 0, Width, 165), 16);
            using var edge = new Pen(Theme.Border); g.DrawLine(edge, Width - 1, 0, Width - 1, Height);
        }
        else if (Style == AtmosphereStyle.Masthead)
        {
            float scale = DeviceDpi / 96f;
            Atmosphere.Ink(g, new(20 * scale, 21 * scale, 144 * scale, 54 * scale), 72);
            Atmosphere.Rain(g, new(Width / 3, 0, Width * 2 / 3, Height), 10);
            using var rule = new Pen(Color.FromArgb(120, Theme.Accent));
            g.DrawLine(rule, 28 * scale, Height - 4 * scale, Width - 28 * scale, Height - 4 * scale);
        }
        else
        {
            Atmosphere.Rain(g, new(Width / 2, 0, Width / 2, Height), 12);
            using var edge = new Pen(Theme.Border); g.DrawLine(edge, 0, 0, Width, 0);
            using var accent = new Pen(Theme.Accent, 2); g.DrawLine(accent, 28, 0, 91, 0);
        }
        Atmosphere.Grain(g, ClientRectangle);
    }
}

internal sealed class LauncherBrand : Control
{
    internal LauncherBrand()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "PZLauncher Community Edition";
        Text = AccessibleName;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f; g.ScaleTransform(scale, scale);
        Atmosphere.Ink(g, new(2, 10, 139, 71), 225);
        using var title = new Font("Impact", 48, FontStyle.Regular, GraphicsUnit.Pixel);
        using var sub = new Font("Impact", 27, FontStyle.Regular, GraphicsUnit.Pixel);
        using var note = new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);
        
        using var white = new SolidBrush(Theme.Text); using var muted = new SolidBrush(Theme.Muted);
        g.DrawString("PZ", title, white, 9, 4);
        g.DrawString("LAUNCHER", sub, white, 10, 56);
        g.DrawString("COMMUNITY EDITION", note, muted, 12, 91);
        g.Restore(saved);
        base.OnPaint(e);
    }
}
