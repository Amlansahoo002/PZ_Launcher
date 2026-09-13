namespace PZLauncher.Desktop;

internal sealed class ResponsiveToolbar : FlowLayoutPanel
{
    internal ResponsiveToolbar() { WrapContents = true; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; MinimumSize = new Size(0, 40); }
    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width is > 0 and < int.MaxValue ? proposedSize.Width : Width;
        int available = Math.Max(1, width - Padding.Horizontal), x = 0, y = 0, row = 0;
        foreach (Control child in Controls)
        {
            if (Visible && !child.Visible) continue;
            int w = child.Width + child.Margin.Horizontal, h = child.Height + child.Margin.Vertical;
            if (x > 0 && x + w > available) { y += row; row = 0; x = 0; }
            x += w; row = Math.Max(row, h);
            if (GetFlowBreak(child)) { y += row; row = 0; x = 0; }
        }
        return new Size(width, Math.Max(MinimumSize.Height, y + row + Padding.Vertical));
    }
}
