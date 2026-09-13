using FluentFTP;

namespace PZLauncher.Desktop;

internal sealed class FtpWorldSettings
{
    internal string Host { get; init; } = "";
    internal int Port { get; init; } = 21;
    internal string User { get; init; } = "";
    internal string Password { get; init; } = "";
    internal string Directory { get; init; } = "/";
    internal FtpEncryptionMode Encryption { get; init; }
    internal FtpWorldSettings At(string path) => new() { Host = Host, Port = Port, User = User, Password = Password, Directory = path, Encryption = Encryption };
}

internal sealed record RemoteWorldEntry(string Name, bool Directory, long Size);
internal interface IWorldConnection : IDisposable
{
    Task<List<RemoteWorldEntry>> List(string relative, CancellationToken token);
    Task Download(string relative, string local, CancellationToken token);
    Task Upload(string local, string relative, CancellationToken token);
    Task Move(string from, string to, CancellationToken token);
    Task<bool> Exists(string relative, CancellationToken token);
    Task Delete(string relative, CancellationToken token);
}

internal sealed class FtpWorldConnection : IWorldConnection
{
    private readonly AsyncFtpClient client;
    private readonly string root;
    internal FtpWorldConnection(FtpWorldSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || settings.Host.IndexOfAny(['/', '\\', '\r', '\n', '@']) >= 0)
            throw new IOException(FtpText.Get("hostInvalid"));
        root = RemoteDirectory(settings.Directory);
        client = new AsyncFtpClient(settings.Host.Trim(), settings.User, settings.Password, settings.Port);
        client.Config.EncryptionMode = settings.Encryption;
        client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        client.Config.ConnectTimeout = client.Config.ReadTimeout = client.Config.DataConnectionConnectTimeout = client.Config.DataConnectionReadTimeout = 15000;
        
        client.Config.ValidateAnyCertificate = false;
    }
    internal static string RemoteDirectory(string path)
    {
        if (path.IndexOfAny(['\\', '\r', '\n', '\0']) >= 0 || path.Split('/').Any(p => p is "." or ".."))
            throw new IOException(FtpText.Get("pathInvalid"));
        return "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }
    internal static string Relative(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('/') || path.IndexOfAny(['\\', '\r', '\n', '\0', ':']) >= 0)
            throw new IOException(FtpText.Get("pathInvalid"));
        foreach (string part in path.Split('/'))
        {
            string stem = part.Split('.')[0];
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                System.Text.RegularExpressions.Regex.IsMatch(stem, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new IOException(FtpText.Get("pathInvalid"));
        }
        return path;
    }
    private string PathFor(string relative) => root.TrimEnd('/') + "/" + Relative(relative);
    private async Task Connect(CancellationToken token)
    { if (!client.IsConnected) await client.Connect(token).ConfigureAwait(false); }
    public async Task<List<RemoteWorldEntry>> List(string relative, CancellationToken token)
    {
        await Connect(token).ConfigureAwait(false);
        var items = await client.GetListing(relative.Length == 0 ? root : PathFor(relative), token).ConfigureAwait(false);
        var result = new List<RemoteWorldEntry>();
        foreach (var item in items)
        {
            if (item.Name is "." or "..") continue;
            Relative(item.Name);
            if (item.Type == FtpObjectType.Link) throw new IOException(FtpText.Get("link", item.Name));
            if (item.Type is FtpObjectType.File or FtpObjectType.Directory)
                result.Add(new(item.Name, item.Type == FtpObjectType.Directory, item.Size));
        }
        return result;
    }
    public async Task Download(string relative, string local, CancellationToken token)
    {
        await Connect(token).ConfigureAwait(false);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(local)!);
        var result = await client.DownloadFile(local, PathFor(relative), FtpLocalExists.Overwrite, token: token).ConfigureAwait(false);
        if (result != FtpStatus.Success) throw new IOException(FtpText.Get("transfer", relative));
    }
    public async Task Upload(string local, string relative, CancellationToken token)
    {
        await Connect(token).ConfigureAwait(false);
        if (await client.FileExists(PathFor(relative), token).ConfigureAwait(false)) throw new IOException(FtpText.Get("conflict", relative));
        var result = await client.UploadFile(local, PathFor(relative), FtpRemoteExists.Skip, token: token).ConfigureAwait(false);
        if (result != FtpStatus.Success) throw new IOException(FtpText.Get("transfer", relative));
    }
    public async Task Move(string from, string to, CancellationToken token)
    {
        await Connect(token).ConfigureAwait(false);
        if (!await client.MoveFile(PathFor(from), PathFor(to), FtpRemoteExists.Skip, token).ConfigureAwait(false))
            throw new IOException(FtpText.Get("transfer", from));
    }
    public async Task<bool> Exists(string relative, CancellationToken token)
    { await Connect(token).ConfigureAwait(false); return await client.FileExists(PathFor(relative), token).ConfigureAwait(false); }
    public async Task Delete(string relative, CancellationToken token)
    { await Connect(token).ConfigureAwait(false); await client.DeleteFile(PathFor(relative), token).ConfigureAwait(false); }
    public void Dispose() => client.Dispose();
}
