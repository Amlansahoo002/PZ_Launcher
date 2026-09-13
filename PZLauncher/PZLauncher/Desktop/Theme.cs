using System.Drawing.Drawing2D;
using System.Drawing.Text;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class Theme
{
    
    
    internal static readonly Color Background = Color.FromArgb(13, 25, 31);
    internal static readonly Color Sidebar = Color.FromArgb(8, 16, 21);
    internal static readonly Color Surface = Color.FromArgb(21, 37, 45);
    internal static readonly Color Hover = Color.FromArgb(34, 55, 65);
    internal static readonly Color Border = Color.FromArgb(49, 70, 79);
    internal static readonly Color ControlBorder = Color.FromArgb(125, 149, 157);
    internal static readonly Color Text = Color.FromArgb(242, 243, 231);
    internal static readonly Color Muted = Color.FromArgb(163, 183, 190);
    internal static readonly Color Accent = Color.FromArgb(205, 0, 59);
    internal static readonly Color AccentHover = Color.FromArgb(227, 20, 77);
    internal static readonly Color AccentText = Color.FromArgb(255, 133, 162);
    internal static readonly Color Warning = Color.FromArgb(228, 184, 113);
    internal static readonly Color Success = Color.FromArgb(177, 198, 172);
    private static string BodyFamily => L.Culture.TwoLetterISOLanguageName switch
    {
        "zh" when L.Culture.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase) || L.Culture.Name is "zh-TW" or "zh-HK" or "zh-MO" => "Microsoft JhengHei UI",
        "zh" => "Microsoft YaHei UI", "ja" => "Yu Gothic UI", "ko" => "Malgun Gothic", _ => "Segoe UI"
    };
    internal static Font Font(float size = 10, bool bold = false) => new(BodyFamily, size, bold ? FontStyle.Bold : FontStyle.Regular);
    internal static Font Heading(float size = 26) => BodyFamily == "Segoe UI"
        ? new("Impact", size, FontStyle.Regular) : Font(size, true);
    internal static GraphicsPath Round(RectangleF rect, float radius = 3)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(rect); return path; }
        float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
    internal static Label Label(string text, float size = 10, bool bold = false, Color? color = null) => new()
    {
        Text = text, Font = Font(size, bold), ForeColor = color ?? Text,
        BackColor = Color.Transparent, AutoSize = false, AutoEllipsis = true,
        UseMnemonic = false
    };
    internal static void Cover(Graphics graphics, Image image, Rectangle bounds)
    {
        float scale = Math.Max((float)bounds.Width / image.Width, (float)bounds.Height / image.Height);
        var target = new RectangleF(bounds.X + (bounds.Width - image.Width * scale) / 2,
            bounds.Y + (bounds.Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
        graphics.DrawImage(image, target);
    }
    internal static void DrawCheck(Graphics graphics, Rectangle bounds, bool check, bool enabled, bool hover = false)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Round(new RectangleF(bounds.X + .5f, bounds.Y + .5f, bounds.Width - 1, bounds.Height - 1), 2);
        using var fill = new SolidBrush(check ? enabled ? Accent : Muted : hover ? Hover : Background);
        using var outline = new Pen(check ? enabled ? Accent : Muted : hover ? AccentText : ControlBorder, 1.3f);
        graphics.FillPath(fill, path); graphics.DrawPath(outline, path);
        if (check)
        {
            using var tick = new Pen(check && enabled ? Text : Background, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            float x = bounds.X, y = bounds.Y, size = bounds.Width;
            graphics.DrawLines(tick, new PointF[] { new(x + size * .24f, y + size * .51f), new(x + size * .44f, y + size * .70f), new(x + size * .77f, y + size * .30f) });
        }
        graphics.Restore(state);
    }
    internal static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            BackgroundColor = Surface, BorderStyle = BorderStyle.None, GridColor = Border,
            RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            EnableHeadersVisualStyles = false, ColumnHeadersHeight = 42,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            Font = Font(10), CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            Dock = DockStyle.Fill
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface, ForeColor = Text, SelectionBackColor = Hover,
            SelectionForeColor = Text, Padding = new Padding(10, 0, 10, 0)
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Background, ForeColor = Muted, Font = Font(9, true), Padding = new Padding(10, 0, 10, 0)
        };
        grid.RowTemplate.Height = 49;
        grid.ColumnAdded += (_, e) =>
        {
            var column = e.Column;
            int headerWidth = TextRenderer.MeasureText(column.HeaderText, grid.ColumnHeadersDefaultCellStyle.Font,
                Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + 28;
            column.MinimumWidth = Math.Max(50, headerWidth);
            if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill) column.Width = Math.Max(column.Width, headerWidth);
        };
        return grid;
    }
}

internal sealed class StyledCheckBox : CheckBox
{
    private bool hover;
    public StyledCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand; UseMnemonic = false;
        MouseEnter += (_, _) => { hover = true; Invalidate(); };
        MouseLeave += (_, _) => { hover = false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        int size = LogicalToDeviceUnits(18), gap = LogicalToDeviceUnits(9);
        Theme.DrawCheck(e.Graphics, new Rectangle(1, (Height - size) / 2, size, size), Checked, Enabled, hover);
        var textArea = new Rectangle(size + gap, 0, Math.Max(1, Width - size - gap - 2), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textArea, Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -2), Theme.Accent, BackColor);
    }
}

internal class SurfacePanel : Panel
{
    internal Color Fill = Theme.Surface;
    internal bool Outline = true;
    public SurfacePanel()
    {
        DoubleBuffered = true;
        Size = new Size(300, 300);
        BackColor = Theme.Background;
        Padding = new Padding(20);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 0);
        using var brush = new SolidBrush(Fill);
        e.Graphics.FillPath(brush, path);
        if (Outline)
        {
            using var pen = new Pen(Theme.Border); e.Graphics.DrawPath(pen, path);
            using var rule = new Pen(Color.FromArgb(125, Theme.ControlBorder));
            e.Graphics.DrawLine(rule, 1, 1, Math.Min(Width - 2, 52), 1);
        }
        base.OnPaint(e);
    }
}

internal sealed class ActionButton : Button
{
    internal bool Primary;
    internal bool Active;
    internal bool Navigation;
    internal bool Tab;
    internal int IconCode = -1;
    internal Rectangle TextArea
    {
        get
        {
            int inset = Width <= 48 ? 4 : 12;
            return new Rectangle(IconCode >= 0 ? 48 : inset, 0, Math.Max(1, Width - (IconCode >= 0 ? 55 : inset * 2)), Height);
        }
    }
    private bool hover;
    public ActionButton(string text = "")
    {
        Text = text; Font = Theme.Font(10, true); Size = new Size(150, 42);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        BackColor = Theme.Surface; ForeColor = Theme.Text; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        MouseEnter += (_, _) => { hover = true; Invalidate(); };
        MouseLeave += (_, _) => { hover = false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        Color fill = Primary ? (hover ? Theme.AccentHover : Theme.Accent) :
            Tab && Active ? Theme.Text : Active && Navigation ? Color.FromArgb(80, 18, 39) :
            Active || hover ? Theme.Hover : Tab ? Theme.Background : Navigation ? Theme.Sidebar : Theme.Surface;
        Color text = !Enabled ? Theme.Muted : Tab && Active ? Theme.Sidebar : Theme.Text;
        if (!Enabled && (Primary || Tab && Active)) fill = Theme.Hover;
        using var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 0);
        using var brush = new SolidBrush(fill); g.FillPath(brush, path);
        if (!Navigation && !Primary && !Tab) { using var border = new Pen(hover ? Theme.ControlBorder : Theme.Border); g.DrawPath(border, path); }
        if (Primary)
        {
            using var edge = new Pen(Color.FromArgb(115, Theme.Text));
            g.DrawLine(edge, 1, 1, Width - 2, 1);
            using var shadow = new Pen(Color.FromArgb(90, Theme.Sidebar), 2);
            g.DrawLine(shadow, 1, Height - 2, Width - 2, Height - 2);
        }
        if (Navigation)
        {
            using var rule = new Pen(Active ? Theme.Accent : Theme.Border);
            g.DrawLine(rule, 15, Height - 1, Width - 10, Height - 1);
            if (Active)
            {
                using var accent = new SolidBrush(Theme.Accent); g.FillRectangle(accent, 0, 0, 4, Height);
                g.FillPolygon(accent, new Point[] { new(Width - 8, 0), new(Width, 0), new(Width, 8) });
            }
        }
        if (Tab)
        {
            using var line = new SolidBrush(Active ? Theme.Accent : Theme.Border);
            g.FillRectangle(line, 0, Active ? 0 : Height - 1, Width, Active ? 3 : 1);
        }
        Rectangle textBounds = TextArea;
        if (IconCode >= 0) DrawIcon(g, IconCode, new Point(19, Height / 2), text);
        TextRenderer.DrawText(g, Navigation ? Text.ToUpper(L.Culture) : Text, Font, textBounds, text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak |
            (Navigation || TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5), text, fill);
    }
    private static void DrawIcon(Graphics g, int icon, Point p, Color color)
    {
        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        int x = p.X, y = p.Y - 8;
        switch (icon)
        {
            case 0: g.DrawPolygon(pen, [new(x + 2, y), new(x + 14, y + 8), new(x + 2, y + 16)]); break;
            case 1:
                g.DrawRectangle(pen, x, y, 6, 6); g.DrawRectangle(pen, x + 10, y, 6, 6);
                g.DrawRectangle(pen, x, y + 10, 6, 6); g.DrawRectangle(pen, x + 10, y + 10, 6, 6); break;
            case 2: g.DrawPolygon(pen, [new(x, y + 3), new(x + 6, y + 3), new(x + 8, y + 6), new(x + 17, y + 6), new(x + 17, y + 16), new(x, y + 16)]); break;
            case 3:
                for (int i = 0; i < 3; i++) { int yy = y + i * 7; g.DrawLine(pen, x, yy, x + 17, yy); g.DrawEllipse(pen, x + (i % 2 == 0 ? 4 : 10), yy - 2, 4, 4); } break;
            case 4: g.DrawRectangle(pen, x, y, 17, 17); g.DrawLine(pen, x + 4, y + 5, x + 13, y + 5); g.DrawLine(pen, x + 4, y + 9, x + 13, y + 9); g.DrawLine(pen, x + 4, y + 13, x + 10, y + 13); break;
            case 5: g.DrawEllipse(pen, x, y, 17, 17); g.DrawEllipse(pen, x + 5, y, 7, 17); g.DrawLine(pen, x, y + 8, x + 17, y + 8); break;
            case 6:
                g.DrawRectangle(pen, x, y, 17, 7); g.DrawRectangle(pen, x, y + 10, 17, 7);
                g.DrawEllipse(pen, x + 3, y + 3, 1, 1); g.DrawEllipse(pen, x + 3, y + 13, 1, 1); break;
            case 7:
                g.DrawRectangle(pen, x, y, 17, 17);
                g.DrawLines(pen, [new(x + 2, y + 9), new(x + 5, y + 9), new(x + 7, y + 4), new(x + 10, y + 13), new(x + 12, y + 9), new(x + 15, y + 9)]); break;
            default: g.DrawEllipse(pen, x, y, 17, 17); g.DrawLine(pen, x + 8, y + 5, x + 8, y + 12); break;
        }
    }
}

internal sealed class HeroCard : Control
{
    internal NewsItem? Item;
    internal Image? CoverImage;
    public HeroCard()
    {
        Height = 285; Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.Link;
        AccessibleName = T("hero.accessible");
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        using var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 0);
        var state = g.Save(); g.SetClip(path);
        using (var background = new LinearGradientBrush(ClientRectangle, Color.FromArgb(42, 82, 101), Theme.Sidebar, 25f))
            g.FillRectangle(background, ClientRectangle);
        if (CoverImage != null) Theme.Cover(g, CoverImage, ClientRectangle);
        else
        {
            Atmosphere.Skyline(g, ClientRectangle);
            Atmosphere.Rain(g, ClientRectangle, 44);
        }
        using (var shade = new LinearGradientBrush(ClientRectangle, Color.FromArgb(235, Theme.Sidebar), Color.FromArgb(25, Theme.Sidebar), 0f))
            g.FillRectangle(shade, ClientRectangle);
        using (var shade = new LinearGradientBrush(ClientRectangle, Color.Transparent, Color.FromArgb(220, Theme.Sidebar), 90f))
            g.FillRectangle(shade, ClientRectangle);
        Atmosphere.Grain(g, ClientRectangle, 19);
        using (var rule = new SolidBrush(Theme.Accent)) g.FillRectangle(rule, 0, 0, Width, 3);
        int textWidth = Math.Min(660, Width - 80);
        using var tag = Theme.Font(9, true); using var detail = Theme.Font(10);
        TextRenderer.DrawText(g, T("hero.tag"), tag, new Rectangle(30, 28, textWidth, 25), Theme.Text, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        string title = (Item?.Title ?? T("hero.empty")).ToUpper(L.Culture);
        var titleArea = new Rectangle(28, 70, textWidth, Math.Max(48, Height - 147));
        using var heading = FitHeading(title, titleArea.Size);
        TextRenderer.DrawText(g, title, heading, titleArea, Theme.Text,
            TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        string source = Item == null ? T("hero.source") :
            "THE INDIE STONE   /   " + Item.Published.ToLocalTime().ToString("dd MMMM yyyy", L.Culture);
        TextRenderer.DrawText(g, source, detail, new Rectangle(30, Height - 65, textWidth, 25), Theme.Text, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Item == null ? T("hero.welcome") : T("hero.read"), tag,
            new Rectangle(30, Height - 34, textWidth, 22), Theme.AccentText, TextFormatFlags.NoPadding);
        using (var rule = new Pen(Color.FromArgb(120, Theme.ControlBorder)))
            g.DrawLine(rule, 30, Height - 77, Width - 30, Height - 77);
        using (var arrow = new Pen(Theme.Text, 1.5f))
        {
            g.DrawLine(arrow, Width - 46, Height - 32, Width - 30, Height - 48);
            g.DrawLines(arrow, new Point[] { new(Width - 41, Height - 48), new(Width - 30, Height - 48), new(Width - 30, Height - 37) });
        }
        g.Restore(state);
        using var outline = new Pen(Theme.Border); g.DrawPath(outline, path);
        if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
    private static Font FitHeading(string text, Size available)
    {
        for (float size = 28; size >= 18; size--)
        {
            var font = Theme.Heading(size);
            var measured = TextRenderer.MeasureText(text, font, new Size(available.Width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (measured.Height <= available.Height || size == 18) return font;
            font.Dispose();
        }
        return Theme.Heading(18);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
    }
}
