namespace PZLauncher.Services;
internal sealed class NewsItem
{
    public string Source { get; init; } = "The Indie Stone";
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public string Summary { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public DateTimeOffset Published { get; init; }
    public override string ToString() => Title;
}
