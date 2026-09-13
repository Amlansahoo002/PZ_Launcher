using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal static class CommunityLinks
{
    internal const string Leaf = "https://github.com/aoqia194/leaf-loader";
    internal const string ZombieBuddy = "https://github.com/zed-0xff/ZombieBuddy";
    internal const string Discord = "https://discord.gg/theindiestone";
    
    internal static readonly string LauncherRepository = "";
    internal static string Repository(string loader) => loader switch
    {
        JavaMods.Leaf => "aoqia194/leaf-loader", JavaMods.ZombieBuddy => "zed-0xff/ZombieBuddy",
        _ => throw new InvalidDataException(T("loaderDownload.unsupported"))
    };
}
internal sealed record LoaderAsset(string Name, string Url, long Size, string Hash = "", string Algorithm = "SHA256");
internal sealed record LoaderRelease(string Loader, string Tag, string Url, List<LoaderAsset> Assets, string SourceZip)
{
    public override string ToString() => Tag;
}
internal sealed record LoaderTransfer(string File, long Received, long? Total);
internal sealed record LoaderInstallation(string Loader, string Version, string RuntimePath, string Directory, string Source);

internal sealed class JavaLoaderDownloads : IDisposable
{
    private const long FileLimit = 200_000_000;
    private readonly HttpClient http;
    private readonly bool ownsClient;
    internal JavaLoaderDownloads(HttpClient? client = null)
    {
        ownsClient = client == null; http = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PZLauncher/0.19.0");
    }
    internal async Task<List<LoaderRelease>> ReleasesAsync(string loader, CancellationToken token)
    {
        string repo = CommunityLinks.Repository(loader);
        using var response = await http.GetAsync($"https://api.github.com/repos/{repo}/releases?per_page=30", HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        EnsureSuccess(response);
        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[16384]; int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > 5_000_000) throw new InvalidDataException(T("loaderDownload.invalid"));
            memory.Write(buffer, 0, read);
        }
        return ParseReleases(loader, System.Text.Encoding.UTF8.GetString(memory.ToArray()));
    }
    internal static List<LoaderRelease> ParseReleases(string loader, string json)
    {
        string repo = CommunityLinks.Repository(loader);
        using var document = JsonDocument.Parse(json);
        var releases = new List<LoaderRelease>();
        foreach (var release in document.RootElement.EnumerateArray().Take(30))
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+(?:\.\d+)?$")) continue;
            var assets = new List<LoaderAsset>();
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                bool wanted = loader == JavaMods.Leaf ? name == $"loader-{tag.TrimStart('v')}.jar"
                    : name is "ZombieBuddy.jar" or "ZombieBuddy.jar.zbs" or "zbNative.dll";
                if (!wanted) continue;
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (!url.StartsWith($"https://github.com/{repo}/releases/download/{Uri.EscapeDataString(tag)}/", StringComparison.Ordinal))
                    throw new InvalidDataException(T("loaderDownload.invalid"));
                long size = asset.GetProperty("size").GetInt64();
                string digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
                if (size is <= 0 or > FileLimit || digest.Length > 0 && !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
                    throw new InvalidDataException(T("loaderDownload.invalid"));
                assets.Add(new(name, url, size, digest.Length > 0 ? digest[7..] : ""));
            }
            string required = loader == JavaMods.Leaf ? $"loader-{tag.TrimStart('v')}.jar" : "ZombieBuddy.jar";
            if (assets.Count(a => a.Name == required) != 1 || assets.Select(a => a.Name).Distinct().Count() != assets.Count) continue;
            releases.Add(new(loader, tag, $"https://github.com/{repo}/releases/tag/{Uri.EscapeDataString(tag)}", assets,
                $"https://api.github.com/repos/{repo}/zipball/{Uri.EscapeDataString(tag)}"));
        }
        return releases.OrderByDescending(r => Version.Parse(r.Tag.TrimStart('v'))).ToList();
    }
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new IOException(T("loaderDownload.rateLimit"));
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme is string scheme && scheme != "https")
            throw new InvalidDataException(T("loaderDownload.invalid"));
    }
    private async Task DownloadAsync(LoaderAsset asset, string destination, IProgress<LoaderTransfer>? progress, CancellationToken token)
    {
        using var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        EnsureSuccess(response);
        long? total = response.Content.Headers.ContentLength;
        if (total > FileLimit || asset.Size > 0 && total.HasValue && total != asset.Size) throw new InvalidDataException(T("loaderDownload.invalid"));
        progress?.Report(new(asset.Name, 0, total ?? (asset.Size > 0 ? asset.Size : null)));
        using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        await using var outputCleanup = output.ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(new HashAlgorithmName(asset.Algorithm));
        byte[] buffer = new byte[65536]; long received = 0; int read; long lastProgress = 0;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            received += read;
            if (received > FileLimit || asset.Size > 0 && received > asset.Size) throw new InvalidDataException(T("loaderDownload.invalid"));
            hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            if (Environment.TickCount64 - lastProgress >= 100)
            { progress?.Report(new(asset.Name, received, total)); lastProgress = Environment.TickCount64; }
        }
        if (received == 0 || total.HasValue && received != total || asset.Size > 0 && received != asset.Size)
            throw new InvalidDataException(T("loaderDownload.invalid"));
        string actual = Convert.ToHexString(hash.GetHashAndReset());
        if (asset.Hash.Length > 0 && !actual.Equals(asset.Hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(T("loaderDownload.hash", asset.Name));
        progress?.Report(new(asset.Name, received, received));
    }
    internal static List<LoaderAsset> LeafLibraries(string jar)
    {
        using var archive = ZipFile.OpenRead(jar);
        var manifest = archive.GetEntry("leaf-installer.json");
        if (manifest == null || manifest.Length > 262144) throw new InvalidDataException(T("java.leafLibraries"));
        using var input = manifest.Open(); using var json = JsonDocument.Parse(input);
        if (json.RootElement.GetProperty("mainClass").GetProperty("client").GetString() != "dev.aoqia.leaf.loader.impl.launch.knot.KnotClient")
            throw new InvalidDataException(T("java.leafLibraries"));
        var libraries = json.RootElement.GetProperty("libraries");
        var result = new List<LoaderAsset>();
        foreach (var library in libraries.GetProperty("common").EnumerateArray().Concat(libraries.GetProperty("client").EnumerateArray()))
        {
            string name = library.GetProperty("name").GetString() ?? "";
            string[] parts = name.Split(':');
            if (parts.Length != 3 || parts.Any(p => !Regex.IsMatch(p, @"^[a-zA-Z0-9_][a-zA-Z0-9_.+-]*$")) || result.Count >= 64)
                throw new InvalidDataException(T("java.leafLibraries"));
            string basis = library.GetProperty("url").GetString() ?? "";
            if (!Uri.TryCreate(basis, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0)
                throw new InvalidDataException(T("java.leafLibraries"));
            string filename = $"{parts[1]}-{parts[2]}.jar";
            string url = basis.TrimEnd('/') + $"/{parts[0].Replace('.', '/')}/{parts[1]}/{parts[2]}/{filename}";
            string algorithm = library.TryGetProperty("sha256", out var digest) ? "SHA256" : "SHA1";
            if (algorithm == "SHA1") library.TryGetProperty("sha1", out digest);
            string hash = digest.ValueKind == JsonValueKind.String ? digest.GetString()! : "";
            if (!Regex.IsMatch(hash, algorithm == "SHA256" ? "^[a-fA-F0-9]{64}$" : "^[a-fA-F0-9]{40}$"))
                throw new InvalidDataException(T("java.leafLibraries"));
            long size = library.TryGetProperty("size", out var length) ? length.GetInt64() : 0;
            if (size is < 0 or > FileLimit || result.Any(a => a.Name.Equals(filename, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(T("java.leafLibraries"));
            result.Add(new(filename, url, size, hash, algorithm));
        }
        return result;
    }
    internal async Task<LoaderInstallation> InstallAsync(LoaderRelease release, string cache, string installation,
        IProgress<LoaderTransfer>? progress, CancellationToken token)
    {
        string root = WorldFiles.Within(cache, ".pzlauncher-loaders"); Directory.CreateDirectory(root);
        using var installationLock = new FileStream(WorldFiles.Within(root, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string staging = WorldFiles.Within(root, "staging/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        try
        {
            
            var releases = await ReleasesAsync(release.Loader, token).ConfigureAwait(false);
            release = releases.SingleOrDefault(r => r.Tag == release.Tag) ?? throw new IOException(T("loaderDownload.noReleases"));
            string runtime = WorldFiles.Within(staging, "runtime"); Directory.CreateDirectory(runtime);
            foreach (var asset in release.Assets)
                await DownloadAsync(asset, WorldFiles.Within(runtime, asset.Name), progress, token).ConfigureAwait(false);
            if (release.Loader == JavaMods.Leaf)
            {
                var libraries = LeafLibraries(Directory.GetFiles(runtime, "*.jar").Single());
                foreach (var asset in libraries)
                    await DownloadAsync(asset, WorldFiles.Within(runtime, asset.Name), progress, token).ConfigureAwait(false);
            }
            else
            {
                string source = WorldFiles.Within(staging, "source.zip");
                await DownloadAsync(new("ZombieBuddy · " + release.Tag, release.SourceZip, 0), source, progress, token).ConfigureAwait(false);
                string companion = WorldFiles.Within(staging, "companion"); Directory.CreateDirectory(companion);
                ExtractCompanion(source, companion, token);
                string libs = WorldFiles.Within(companion, "libs"); Directory.CreateDirectory(libs);
                foreach (string file in Directory.GetFiles(runtime)) File.Copy(file, WorldFiles.Within(libs, Path.GetFileName(file)));
            }
            progress?.Report(new(T("loaderDownload.validating"), 0, null));
            var candidate = new PlayerProfile { CachePath = cache, JavaLoader = release.Loader, LeafLibraryPath = runtime, ZombieBuddyAgentPath = Path.Combine(runtime, "ZombieBuddy.jar") };
            JavaMods.Prepare(candidate, [], installation);
            token.ThrowIfCancellationRequested();
            return Commit(release, cache, root, staging, token);
        }
        finally
        {
            if (Directory.Exists(staging)) { WorldFiles.Enumerate(staging); Directory.Delete(WorldFiles.Within(root, Path.GetRelativePath(root, staging)), true); }
        }
    }
    internal static void ExtractCompanion(string zipPath, string target, CancellationToken token)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var infos = zip.Entries.Where(e => e.FullName.EndsWith("/42/mod.info", StringComparison.Ordinal)).ToList();
        if (infos.Count != 1) throw new InvalidDataException(T("loaderDownload.companionMissing"));
        string prefix = infos[0].FullName[..^"42/mod.info".Length];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.Name.Length == 0) continue;
            string relative = entry.FullName[prefix.Length..];
            if (!relative.StartsWith("common/", StringComparison.Ordinal) && !Regex.IsMatch(relative, @"^\d+(?:\.\d+)*/") &&
                relative is not ("LICENSE.txt" or "icon_128.png" or "icon_256.png")) continue;
            total += entry.Length;
            if (total > 100_000_000 || names.Count >= 10000 || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 ||
                relative.Contains(':') || relative.Contains('\\') || relative.Split('/').Any(p => p is "." or ".."))
                throw new InvalidDataException(T("loaderDownload.invalid"));
            string path = WorldFiles.Within(target, relative);
            if (!names.Add(path)) throw new InvalidDataException(T("loaderDownload.invalid"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path);
        }
        if (!HasBuddyInfo(target)) throw new InvalidDataException(T("loaderDownload.companionMissing"));
    }
    private static bool HasBuddyInfo(string folder)
    {
        if (!Directory.Exists(folder)) return false;
        
        
        var layers = new[] { folder, Path.Combine(folder, "common") }.Concat(Directory.EnumerateDirectories(folder)
            .Where(path => Regex.IsMatch(Path.GetFileName(path), @"^\d+(?:\.\d+)*$")));
        return layers.Select(layer => Path.Combine(layer, "mod.info")).Where(File.Exists)
            .Any(file => new FileInfo(file).Length <= 262144 && File.ReadLines(file).Any(line => Regex.IsMatch(line, @"^\s*id\s*=\s*\\?ZombieBuddy\s*$")));
    }

    internal static LoaderInstallation Commit(LoaderRelease release, string cache, string root, string staging, CancellationToken token)
    {
        GameProcessGuard.EnsureStopped(cache);
        string workshopRoot = WorldFiles.Within(cache, ".pzlauncher-workshop"); Directory.CreateDirectory(workshopRoot);
        using var workshopLock = new FileStream(WorldFiles.Within(workshopRoot, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string id = release.Loader + "/" + release.Tag + "-" + Guid.NewGuid().ToString("N")[..8];
        string destination = WorldFiles.Within(root, id); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string mods = WorldFiles.Within(cache, "mods"); Directory.CreateDirectory(mods);
        string target = WorldFiles.Within(mods, "ZombieBuddy");
        string backup = WorldFiles.Within(root, "backups/" + Guid.NewGuid().ToString("N"));
        string index = WorkshopStore.IndexPath(cache); string? originalIndex = null;
        bool oldMoved = false, newMoved = false, runtimeMoved = false, indexChanged = false;
        try
        {
            if (release.Loader == JavaMods.ZombieBuddy)
            {
                foreach (string folder in Directory.GetDirectories(mods))
                    if (!folder.Equals(target, StringComparison.OrdinalIgnoreCase) && HasBuddyInfo(folder))
                        throw new IOException(T("loaderDownload.companionConflict", folder));
                if (Directory.Exists(target) && !HasBuddyInfo(target) || File.Exists(target))
                    throw new IOException(T("loaderDownload.companionConflict", target));
                var tracked = WorkshopStore.Read(cache);
                originalIndex = File.Exists(index) ? File.ReadAllText(index) : null;
                foreach (var item in tracked) item.Mods.RemoveAll(m => m.Folder.Equals("ZombieBuddy", StringComparison.OrdinalIgnoreCase));
                tracked.RemoveAll(i => i.Mods.Count == 0);
                Directory.CreateDirectory(backup);
                if (originalIndex != null) File.WriteAllText(Path.Combine(backup, "workshop-installed.json"), originalIndex);
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(target)) { WorldFiles.Enumerate(target); Directory.Move(target, WorldFiles.Within(backup, "ZombieBuddy")); oldMoved = true; }
                Directory.Move(WorldFiles.Within(staging, "companion"), target); newMoved = true;
                if (originalIndex != null) { LauncherStorage.WriteAtomic(index, JsonSerializer.Serialize(tracked)); indexChanged = true; }
            }
            Directory.Move(staging, destination); runtimeMoved = true;
            string runtime = Path.Combine(destination, "runtime");
            var result = new LoaderInstallation(release.Loader, release.Tag, release.Loader == JavaMods.Leaf ? runtime : Path.Combine(runtime, "ZombieBuddy.jar"), destination, release.Url);
            File.WriteAllText(Path.Combine(destination, "installation.json"), JsonSerializer.Serialize(new
            {
                installedAt = DateTimeOffset.Now, result, assets = release.Assets,
                files = WorldFiles.Enumerate(runtime).Select(f => new { file = Path.GetFileName(f), sha256 = WorldFiles.Hash(f) }),
                companionBackup = oldMoved ? backup : null
            }, new JsonSerializerOptions { WriteIndented = true }));
            return result;
        }
        catch
        {
            if (runtimeMoved) Directory.Move(destination, staging);
            if (newMoved) Directory.Move(target, WorldFiles.Within(staging, "companion"));
            if (oldMoved) Directory.Move(WorldFiles.Within(backup, "ZombieBuddy"), target);
            if (indexChanged && originalIndex != null) LauncherStorage.WriteAtomic(index, originalIndex);
            throw;
        }
    }
    public void Dispose() { if (ownsClient) http.Dispose(); }
}
