namespace PZLauncher.Desktop;

internal sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 800;
    public bool Maximized { get; set; }

    internal Rectangle Fit(Rectangle workingArea, Size minimum)
    {
        int width = Math.Clamp(Width, Math.Min(minimum.Width, workingArea.Width), workingArea.Width);
        int height = Math.Clamp(Height, Math.Min(minimum.Height, workingArea.Height), workingArea.Height);
        return new(Math.Clamp(X, workingArea.Left, workingArea.Right - width),
            Math.Clamp(Y, workingArea.Top, workingArea.Bottom - height), width, height);
    }
}
