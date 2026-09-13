using System.Text.Json;

namespace PZLauncher.Desktop;

internal sealed record RemoteWorldChange(string Relative, string OriginalHash, string? UpdatedHash)
{ internal bool Deleted => UpdatedHash == null; }



internal sealed class FtpWorldWorkspace
{
    internal string LocalRoot { get; }
    internal string RemoteDisplay { get; }
    internal bool IncludesChunks { get; }
    internal bool RequiresReload { get; private set; }
    private readonly string workspace;
    private readonly Func<IWorldConnection> connect;
    private readonly FtpWorldSettings? settings;
    private readonly Dictionary<string, string> baseline = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Layers = ["map", "blam", "isoregiondata", "chunkdata", "zpop", "apop", "metagrid"];
    internal static readonly string[] EditorFiles = ["mods.txt", "map_ver.bin", "map_t.bin", "map_sand.bin"];
    internal FtpWorldWorkspace(string directory, string display, bool chunks, Func<IWorldConnection> factory, FtpWorldSettings? connectionSettings = null)
    {
        workspace = Path.GetFullPath(directory); LocalRoot = Path.Combine(workspace, "world");
        RemoteDisplay = display; IncludesChunks = chunks; connect = factory; settings = connectionSettings;
    }
    private static bool RootFile(string name, bool chunks) => EditorFiles.Contains(name) || name.EndsWith(".db", StringComparison.Ordinal) ||
        chunks && System.Text.RegularExpressions.Regex.IsMatch(name, @"^(chunkdata|zpop|apop|metacell)_-?\d+_-?\d+\.bin$");
    private static void RejectLiveDatabases(IEnumerable<RemoteWorldEntry> items)
    {
        if (items.Any(i => !i.Directory && i.Size != 0 && (i.Name.EndsWith(".db-wal") || i.Name.EndsWith(".db-journal"))))
            throw new IOException(FtpText.Get("databaseActive"));
    }
    internal async Task Download(IProgress<string>? progress, CancellationToken token)
    {
        if (Directory.Exists(LocalRoot)) throw new IOException(FtpText.Get("freshCopy"));
        Directory.CreateDirectory(LocalRoot);
        using var remote = connect();
        var root = await remote.List("", token).ConfigureAwait(false); RejectLiveDatabases(root);
        if (!root.Any(i => !i.Directory && EditorFiles.Contains(i.Name)) && !root.Any(i => i.Directory && i.Name == "map"))
            throw new IOException(FtpText.Get("notWorld"));
        var files = new List<string>(); var pending = new Queue<string>();
        foreach (var entry in root)
        {
            if (!entry.Directory && RootFile(entry.Name, IncludesChunks)) files.Add(FtpWorldConnection.Relative(entry.Name));
            if (entry.Directory && IncludesChunks && Layers.Contains(entry.Name)) pending.Enqueue(entry.Name);
        }
        while (pending.TryDequeue(out string? directory))
        {
            token.ThrowIfCancellationRequested(); progress?.Report(FtpText.Get("listing", directory));
            Directory.CreateDirectory(WorldFiles.Within(LocalRoot, directory));
            foreach (var entry in await remote.List(directory, token).ConfigureAwait(false))
            {
                string relative = FtpWorldConnection.Relative(directory + "/" + entry.Name);
                if (entry.Directory) pending.Enqueue(relative);
                else if (entry.Name.EndsWith(".bin", StringComparison.Ordinal)) files.Add(relative);
            }
        }
        if (files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count) throw new IOException(FtpText.Get("pathInvalid"));
        for (int i = 0; i < files.Count; i++)
        {
            string name = files[i]; progress?.Report($"{i + 1}/{files.Count} · {name}"); token.ThrowIfCancellationRequested();
            string local = WorldFiles.Within(LocalRoot, name), original = WorldFiles.Within(Path.Combine(workspace, "original"), name);
            await remote.Download(name, local, token).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(original)!); File.Copy(local, original);
            baseline.Add(name, WorldFiles.Hash(local));
        }
        RejectLiveDatabases(await remote.List("", token).ConfigureAwait(false));
        SaveManifest();
    }
    internal List<RemoteWorldChange> Changes()
    {
        if (RequiresReload) throw new IOException(FtpText.Get("reloadRequired"));
        RejectLocalDatabaseJournals();
        return baseline.Select(p => { string file = WorldFiles.Within(LocalRoot, p.Key); return new RemoteWorldChange(p.Key, p.Value, File.Exists(file) ? WorldFiles.Hash(file) : null); })
            .Where(c => c.OriginalHash != c.UpdatedHash).ToList();
    }
    private void RejectLocalDatabaseJournals()
    {
        if (Directory.EnumerateFiles(LocalRoot).Any(p => (p.EndsWith(".db-wal") || p.EndsWith(".db-journal")) && new FileInfo(p).Length != 0))
            throw new IOException(FtpText.Get("localDatabaseActive"));
    }
    private void SaveManifest() => LauncherStorage.WriteAtomic(Path.Combine(workspace, "manifest.json"),
        JsonSerializer.Serialize(new { schemaVersion = 1, RemoteDisplay, IncludesChunks, baseline, connection = settings == null ? null :
            new { settings.Host, settings.Port, settings.User, settings.Directory, Encryption = (int)settings.Encryption } }, new JsonSerializerOptions { WriteIndented = true }));
    internal static FtpWorldSettings ReadSettings(string manifest)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new IOException(FtpText.Get("pathInvalid"));
        var saved = root.GetProperty("connection");
        return new() { Host = saved.GetProperty("Host").GetString()!, Port = saved.GetProperty("Port").GetInt32(), User = saved.GetProperty("User").GetString()!,
            Directory = saved.GetProperty("Directory").GetString()!, Encryption = (FluentFTP.FtpEncryptionMode)saved.GetProperty("Encryption").GetInt32() };
    }
    internal static FtpWorldWorkspace Resume(string manifest, FtpWorldSettings settings, Func<IWorldConnection>? factory = null)
    {
        var saved = ReadSettings(manifest);
        if (saved.Host != settings.Host || saved.Port != settings.Port || saved.User != settings.User || saved.Directory != settings.Directory || saved.Encryption != settings.Encryption)
            throw new IOException(FtpText.Get("resumeTarget"));
        string directory = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        
        string transactions = Path.Combine(directory, "transactions");
        if (Directory.Exists(transactions))
            foreach (string path in Directory.EnumerateFiles(transactions, "transaction.json", SearchOption.AllDirectories))
            {
                using var journal = JsonDocument.Parse(File.ReadAllText(path));
                if (journal.RootElement.GetProperty("status").GetString() is "committing" or "recovery-required")
                    throw new IOException(FtpText.Get("recovery", path));
            }
        using var document = JsonDocument.Parse(File.ReadAllText(manifest)); var root = document.RootElement;
        var copy = new FtpWorldWorkspace(directory, root.GetProperty("RemoteDisplay").GetString()!, root.GetProperty("IncludesChunks").GetBoolean(),
            factory ?? (() => new FtpWorldConnection(settings)), settings);
        if (!Directory.Exists(copy.LocalRoot)) throw new IOException(FtpText.Get("freshCopy"));
        WorldFiles.Within(directory, "world");
        foreach (var value in root.GetProperty("baseline").EnumerateObject())
        {
            string relative = FtpWorldConnection.Relative(value.Name), hash = value.Value.GetString()!;
            WorldFiles.Within(copy.LocalRoot, relative);
            if (!System.Text.RegularExpressions.Regex.IsMatch(hash, "^[0-9A-F]{64}$") || !copy.baseline.TryAdd(relative, hash)) throw new IOException(FtpText.Get("pathInvalid"));
        }
        return copy;
    }
    internal async Task<string> Apply(IReadOnlyList<RemoteWorldChange> changes, IProgress<string>? progress, CancellationToken token)
    {
        if (RequiresReload || !Changes().SequenceEqual(changes)) throw new IOException(FtpText.Get("conflict", LocalRoot));
        if (changes.Count == 0) return FtpText.Get("noChanges");
        string id = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string transaction = Path.Combine(workspace, "transactions", id); Directory.CreateDirectory(transaction);
        using var remote = connect();
        var staged = new List<(RemoteWorldChange Change, string Temp, string Backup)>();
        var moved = new List<(RemoteWorldChange Change, string Temp, string Backup)>();
        var replaced = new HashSet<string>();
        void Journal(string status) => LauncherStorage.WriteAtomic(Path.Combine(transaction, "transaction.json"),
            JsonSerializer.Serialize(new { status, RemoteDisplay, staged, moved, replaced }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        async Task VerifyOriginal(RemoteWorldChange change, CancellationToken ct)
        {
            string check = WorldFiles.Within(transaction, "remote/" + change.Relative);
            await remote.Download(change.Relative, check, ct).ConfigureAwait(false);
            if (WorldFiles.Hash(check) != change.OriginalHash) throw new IOException(FtpText.Get("conflict", change.Relative));
        }
        bool committing = false;
        try
        {
            RejectLiveDatabases(await remote.List("", token).ConfigureAwait(false));
            
            foreach (var change in changes) { progress?.Report(FtpText.Get("checking", change.Relative)); await VerifyOriginal(change, token).ConfigureAwait(false); }
            foreach (var change in changes)
            {
                token.ThrowIfCancellationRequested();
                var item = (Change: change, Temp: change.Relative + ".pzlauncher-" + id + ".upload", Backup: change.Relative + ".pzlauncher-" + id + ".bak");
                staged.Add(item); Journal("staging");
                if (change.Deleted) continue;
                string snapshot = WorldFiles.Within(transaction, "updated/" + change.Relative);
                RejectLocalDatabaseJournals();
                Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!); File.Copy(WorldFiles.Within(LocalRoot, change.Relative), snapshot);
                if (WorldFiles.Hash(snapshot) != change.UpdatedHash) throw new IOException(FtpText.Get("conflict", change.Relative));
                progress?.Report(FtpText.Get("sending", change.Relative));
                await remote.Upload(snapshot, item.Temp, token).ConfigureAwait(false);
                string verify = WorldFiles.Within(transaction, "verify/" + change.Relative);
                await remote.Download(item.Temp, verify, token).ConfigureAwait(false);
                if (WorldFiles.Hash(verify) != change.UpdatedHash) throw new IOException(FtpText.Get("transfer", change.Relative));
            }
            RejectLiveDatabases(await remote.List("", token).ConfigureAwait(false));
            foreach (var change in changes) await VerifyOriginal(change, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); committing = true; Journal("committing");
            
            foreach (var item in staged)
            {
                progress?.Report(FtpText.Get("replacing", item.Change.Relative));
                await remote.Move(item.Change.Relative, item.Backup, CancellationToken.None).ConfigureAwait(false);
                moved.Add(item); Journal("committing");
                if (!item.Change.Deleted)
                {
                    await remote.Move(item.Temp, item.Change.Relative, CancellationToken.None).ConfigureAwait(false);
                    replaced.Add(item.Change.Relative); Journal("committing");
                }
            }
            foreach (var item in staged)
            {
                if (item.Change.Deleted) baseline.Remove(item.Change.Relative);
                else baseline[item.Change.Relative] = item.Change.UpdatedHash!;
            }
            SaveManifest(); Journal("complete");
            return FtpText.Get("complete", changes.Count, transaction);
        }
        catch (Exception error)
        {
            var rollbackErrors = new List<string>();
            if (committing)
            {
                RequiresReload = true;
                
                foreach (var item in staged.AsEnumerable().Reverse())
                    try
                    {
                        if (!await remote.Exists(item.Backup, CancellationToken.None).ConfigureAwait(false)) continue;
                        if (await remote.Exists(item.Change.Relative, CancellationToken.None).ConfigureAwait(false))
                            await remote.Move(item.Change.Relative, item.Temp + ".recovery", CancellationToken.None).ConfigureAwait(false);
                        await remote.Move(item.Backup, item.Change.Relative, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex) { rollbackErrors.Add(item.Change.Relative + ": " + ex.Message); }
            }
            Journal(rollbackErrors.Count == 0 ? "aborted" : "recovery-required");
            if (committing) throw new IOException(FtpText.Get(rollbackErrors.Count == 0 ? "rolledBack" : "recovery", transaction) + "\n" + error.Message + "\n" + string.Join('\n', rollbackErrors), error);
            throw;
        }
        finally
        {
            foreach (var item in staged.Where(i => !i.Change.Deleted))
                try { if (await remote.Exists(item.Temp, CancellationToken.None).ConfigureAwait(false)) await remote.Delete(item.Temp, CancellationToken.None).ConfigureAwait(false); }
                catch {  }
        }
    }
}
