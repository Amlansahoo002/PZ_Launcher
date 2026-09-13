namespace PZLauncher.Desktop;

internal sealed class MapPreviewSettings
{
    public string ImagePath { get; set; } = "";
    public Dictionary<string, float> ImageScales { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    internal float ScaleFor(string path)
    {
        float value = ImageScales.FirstOrDefault(item => string.Equals(item.Key, path, StringComparison.OrdinalIgnoreCase)).Value;
        return float.IsFinite(value) && value is >= .125f and <= 64f ? value : 1f;
    }
    internal void Remember(string path, float tilesPerPixel)
    {
        if (!float.IsFinite(tilesPerPixel) || tilesPerPixel is < .125f or > 64f)
            throw new ArgumentOutOfRangeException(nameof(tilesPerPixel));
        ImagePath = Path.GetFullPath(path);
        var existing = ImageScales.Keys.FirstOrDefault(key => string.Equals(key, ImagePath, StringComparison.OrdinalIgnoreCase));
        ImageScales[existing ?? ImagePath] = tilesPerPixel;
    }
}
