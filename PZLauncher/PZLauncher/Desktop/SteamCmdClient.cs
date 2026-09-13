using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal sealed class WorkshopDownloadTracker
{
    private string tail = "";
    internal Dictionary<string, string> Results { get; } = [];
    internal void Append(string chunk)
    {
        string text = tail + chunk;
        foreach (Match match in Regex.Matches(text,
            @"Success\.\s+Downloaded item\s+(?<id>[0-9]+)\s|ERROR!\s*Download item\s+(?<id>[0-9]+)\s+failed\s*\((?<error>[^)\r\n]+)\)", RegexOptions.IgnoreCase))
            Results[match.Groups["id"].Value] = match.Groups["error"].Value;
        tail = text.Length > 8192 ? text[^8192..] : text;
    }
}
internal sealed record WorkshopItemResult(string Id, bool Success, string Detail);

internal sealed class SteamCmdClient(string? root = null)
{
    internal const string AppId = "108600";
    internal const string BootstrapUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
    internal string Root { get; } = root ?? Path.Combine(LauncherStorage.Root, "steamcmd");
    internal string Executable => Path.Combine(Root, "steamcmd.exe");
    private readonly object sync = new();
    private string transcript = "";
    internal string Output { get { lock (sync) return transcript; } }
    internal void Log(string text)
    {
        lock (sync)
        {
            transcript += text.Replace("\r\r\n", "\r\n");
            if (transcript.Length > 120000) transcript = transcript[^100000..];
        }
    }
    internal static string Account(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value == "anonymous") return "anonymous";
        if (!Regex.IsMatch(value, "^[A-Za-z0-9_@.-]{1,64}$")) throw new InvalidDataException(T("workshop.accountInvalid"));
        return value;
    }
    private FileStream Acquire()
    {
        Directory.CreateDirectory(Root);
        try { return new FileStream(Path.Combine(Root, ".pzlauncher.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException(T("workshop.busy"), ex); }
    }
    internal static List<string> DownloadArguments(IEnumerable<string> ids, string account, bool validate)
    {
        var result = new List<string> { "+@NoPromptForPassword", "1", "+login", Account(account) };
        foreach (string id in ids)
        {
            if (!WorkshopStore.ValidId(id)) throw new InvalidDataException(T("workshop.invalidId", id));
            result.AddRange(["+workshop_download_item", AppId, id]);
            if (validate) result.Add("validate");
        }
        result.Add("+quit");
        return result;
    }
    internal async Task EnsureReadyAsync(CancellationToken cancellation)
    {
        if (!File.Exists(Executable))
        {
            Log(T("workshop.installingSteam") + "\n");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            using var response = await http.GetAsync(BootstrapUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string archive = WorldFiles.Within(Root, "bootstrap-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                await using (var target = File.Create(archive))
                await using (var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
                    await input.CopyToAsync(target, cancellation).ConfigureAwait(false);
                using var zip = ZipFile.OpenRead(archive);
                foreach (var entry in zip.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string target = WorldFiles.Within(Root, entry.FullName);
                    if (entry.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target, true);
                }
            }
            finally { if (File.Exists(archive)) File.Delete(archive); }
            if (!File.Exists(Executable)) throw new IOException(T("workshop.steamMissing"));
        }
        Log(T("workshop.updatingSteam") + "\n");
        string output = await CaptureAsync(["+quit"], null, TimeSpan.FromMinutes(5), cancellation).ConfigureAwait(false);
        if (!output.Contains("Steam Console Client", StringComparison.OrdinalIgnoreCase))
            throw new IOException(T("workshop.steamMissing"));
    }
    internal async Task SmokeAsync(CancellationToken cancellation)
    {
        using var operationLock = Acquire();
        await EnsureReadyAsync(cancellation).ConfigureAwait(false);
        var tracker = new WorkshopDownloadTracker();
        string text = await CaptureAsync(DownloadArguments(["18446744073709551614"], "anonymous", true), tracker, TimeSpan.FromMinutes(2), cancellation).ConfigureAwait(false);
        if (!text.Contains("Connecting anonymously") || !tracker.Results.TryGetValue("18446744073709551614", out string? result) || result.Length == 0)
            throw new IOException("SteamCMD smoke check: anonymous connection or per-item failure was not observed.");
    }
    internal async Task LoginAsync(string account, CancellationToken cancellation)
    {
        account = Account(account);
        if (account == "anonymous") throw new InvalidDataException(T("workshop.accountRequired"));
        using var operationLock = Acquire();
        await EnsureReadyAsync(cancellation).ConfigureAwait(false);
        Log(T("workshop.nativeLogin") + "\n");
        var start = new ProcessStartInfo(Executable) { WorkingDirectory = Root, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal };
        start.ArgumentList.Add("+login"); start.ArgumentList.Add(account); start.ArgumentList.Add("+quit");
        using var process = Process.Start(start) ?? throw new IOException(T("error.start"));
        using var job = new WorkshopProcessJob(process);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        using var stop = timeout.Token.Register(job.Dispose);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        Log(T("workshop.loginClosed") + "\n");
    }
    internal async Task<List<WorkshopItemResult>> DownloadAsync(string cache, IReadOnlyList<string> ids, string account, bool validate, CancellationToken cancellation)
    {
        if (ids.Count is 0 or > 200) throw new InvalidDataException(T("workshop.idCount"));
        using var operationLock = Acquire();
        await EnsureReadyAsync(cancellation).ConfigureAwait(false);
        var tracker = new WorkshopDownloadTracker();
        await CaptureAsync(DownloadArguments(ids, account, validate), tracker, TimeSpan.FromMinutes(60), cancellation).ConfigureAwait(false);
        var results = new List<WorkshopItemResult>();
        foreach (string id in ids)
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (!tracker.Results.TryGetValue(id, out string? error)) throw new IOException(T("workshop.notDownloaded"));
                if (error.Length > 0) throw new IOException(error);
                string download = WorldFiles.Within(Root, "steamapps/workshop/content/" + AppId + "/" + id);
                GameProcessGuard.EnsureStopped(cache);
                var installed = await Task.Run(() => WorkshopStore.Install(cache, id, download, cancellation,
                    Path.Combine(Root, "steamapps", "workshop", "appworkshop_108600.acf")), cancellation).ConfigureAwait(false);
                string detail = string.Join(", ", installed.Mods.Select(m => m.Name));
                results.Add(new(id, true, detail)); Log("\n" + T("workshop.installed", id, detail) + "\n");
            }
            catch (OperationCanceledException)
            {
                results.AddRange(ids.Skip(results.Count).Select(pending => new WorkshopItemResult(pending, false, T("workshop.cancelled"))));
                Log("\n" + T("workshop.cancelled") + "\n"); break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                results.Add(new(id, false, ex.Message)); Log("\n" + T("workshop.itemFailed", id, ex.Message) + "\n");
            }
        }
        return results;
    }
    private async Task<string> CaptureAsync(IReadOnlyList<string> arguments, WorkshopDownloadTracker? tracker, TimeSpan limit, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(Executable)
        {
            WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException(T("error.start"));
        using var job = new WorkshopProcessJob(process);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(limit);
        using var stop = timeout.Token.Register(job.Dispose);
        var output = new StringBuilder();
        async Task Read(StreamReader reader, bool track)
        {
            var buffer = new char[2048];
            while (true)
            {
                int count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false);
                if (count == 0) break;
                string chunk = new(buffer, 0, count);
                Log(chunk);
                lock (output)
                {
                    if (track) tracker?.Append(chunk);
                    output.Append(chunk);
                    if (output.Length > 2_000_000) output.Remove(0, output.Length - 1_500_000);
                }
            }
        }
        try
        {
            await Task.WhenAll(Read(process.StandardOutput, true), Read(process.StandardError, false), process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            
            
            return output.ToString();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new IOException(T("workshop.timeout")); }
    }
}
