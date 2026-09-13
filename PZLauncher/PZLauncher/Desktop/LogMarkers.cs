namespace PZLauncher.Desktop;

internal sealed class LogMarkers : Control
{
    internal List<(int Line, string Level)> Marks = [];
    internal int TotalLines = 1;
    internal int SelectedLine;
    internal event Action<int>? Selected;
    internal LogMarkers()
    {
        Width = 25; Dock = DockStyle.Right; BackColor = Theme.Surface; Cursor = Cursors.Hand;
        AccessibleName = T("log.markers"); TabStop = true; DoubleBuffered = true;
    }
    private int Y(int line) => 4 + (int)((long)(line - 1) * Math.Max(1, Height - 9) / Math.Max(1, TotalLines - 1));
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        foreach (var mark in Marks.OrderBy(m => m.Level == "ERROR" ? 1 : 0))
        { using var brush = new SolidBrush(mark.Level == "ERROR" ? Color.IndianRed : Color.Goldenrod); e.Graphics.FillRectangle(brush, 4, Y(mark.Line), Width - 8, 3); }
        if (SelectedLine > 0) { using var pen = new Pen(Theme.Text, 2); e.Graphics.DrawRectangle(pen, 1, Math.Clamp(Y(SelectedLine) - 2, 1, Math.Max(1, Height - 7)), Width - 3, 6); }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    { base.OnMouseDown(e); if (Marks.Count > 0) Selected?.Invoke(Marks.MinBy(m => Math.Abs(Y(m.Line) - e.Y)).Line); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Up or Keys.Down) || Marks.Count == 0) return;
        var lines = Marks.Select(m => m.Line).Order().ToArray();
        Selected?.Invoke(e.KeyCode == Keys.Down ? lines.FirstOrDefault(l => l > SelectedLine, lines[0]) : lines.LastOrDefault(l => l < SelectedLine, lines[^1])); e.Handled = true;
    }
}
