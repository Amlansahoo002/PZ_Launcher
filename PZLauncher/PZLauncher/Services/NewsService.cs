using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PZLauncher.Desktop;
using SkiaSharp;

namespace PZLauncher.Services;

internal sealed class NewsService
{
    public const string FeedUrl = "https://projectzomboid.com/blog/feed/";
    private static readonly HttpClient Http = CreateClient();
    private readonly string cacheRoot;
    public bool FromCache { get; private set; }
    public DateTimeOffset LastUpdated { get; private set; }
    public string Error { get; private set; } = "";
    public NewsService(string? cacheRoot = null) =>
        this.cacheRoot = cacheRoot ?? Path.Combine(LauncherStorage.Root, "news");
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(18) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PZLauncher-Community/0.4");
        return client;
    }
    public IReadOnlyList<NewsItem> GetPinnedItems()
    {
        string path = Path.Combine(cacheRoot, "feed.json");
        try
        {
            if (!File.Exists(path)) return [];
            LastUpdated = File.GetLastWriteTimeUtc(path);
            FromCache = true;
            return JsonSerializer.Deserialize<List<NewsItem>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException) { return []; }
    }
    public async Task<IReadOnlyList<NewsItem>> RefreshPinnedItemsAsync(CancellationToken cancellationToken)
    {
        try
        {
            string xml = await Http.GetStringAsync(FeedUrl, cancellationToken);
            var items = ParseFeed(xml);
            if (items.Count == 0) throw new InvalidDataException(T("news.invalid"));
            Directory.CreateDirectory(cacheRoot);
            LauncherStorage.WriteAtomic(Path.Combine(cacheRoot, "feed.json"), JsonSerializer.Serialize(items));
            FromCache = false;
            Error = "";
            LastUpdated = DateTimeOffset.UtcNow;
            return items;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or XmlException or TaskCanceledException or InvalidDataException)
        {
            Error = T("news.unreachable");
            return GetPinnedItems();
        }
    }
    internal static IReadOnlyList<NewsItem> ParseFeed(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 8_000_000 });
        var document = XDocument.Load(reader);
        XNamespace content = "http://purl.org/rss/1.0/modules/content/";
        return document.Descendants("item").Select(item =>
        {
            string html = item.Element(content + "encoded")?.Value ?? item.Element("description")?.Value ?? "";
            var image = Regex.Match(html, "<img[^>]+src=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            DateTimeOffset.TryParse(item.Element("pubDate")?.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var date);
            return new NewsItem
            {
                Title = Plain(item.Element("title")?.Value ?? "", 140),
                Url = item.Element("link")?.Value ?? "",
                Summary = Plain(item.Element("description")?.Value ?? html, 235),
                ImageUrl = image.Success ? WebUtility.HtmlDecode(image.Groups[1].Value) : "",
                Published = date
            };
        }).Where(i => IsOfficialUrl(i.Url) && i.Title.Length > 0).Take(10).ToList();
    }
    private static string Plain(string html, int length)
    {
        html = Regex.Replace(html, @"<p>The post\b.*?</p>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        string text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromSeconds(1)));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > length ? text[..length].TrimEnd() + "…" : text;
    }
    public static bool IsOfficialUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && (uri.Host == "projectzomboid.com" || uri.Host.EndsWith(".projectzomboid.com") ||
        uri.Host == "theindiestone.com" || uri.Host.EndsWith(".theindiestone.com"));
    public async Task<Image?> GetImageAsync(NewsItem item, CancellationToken token)
    {
        if (!IsOfficialUrl(item.ImageUrl)) return null;
        string path = Path.Combine(cacheRoot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.ImageUrl))) + ".image");
        try
        {
            if (!File.Exists(path))
            {
                using var response = await Http.GetAsync(item.ImageUrl, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 10_000_000) return null;
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                using var bytes = new MemoryStream();
                var buffer = new byte[32768];
                int count;
                while ((count = await stream.ReadAsync(buffer, token)) > 0)
                {
                    if (bytes.Length + count > 10_000_000) return null;
                    bytes.Write(buffer, 0, count);
                }
                Directory.CreateDirectory(cacheRoot);
                await File.WriteAllBytesAsync(path, bytes.ToArray(), token);
            }
            using var decoded = SKBitmap.Decode(path);
            if (decoded == null) return null;
            using var raster = SKImage.FromBitmap(decoded);
            using var png = raster.Encode(SKEncodedImageFormat.Png, 100);
            using var imageStream = png.AsStream();
            using var file = Image.FromStream(imageStream);
            return new Bitmap(file);
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or ArgumentException or OutOfMemoryException)
        { return null; }
    }
}
