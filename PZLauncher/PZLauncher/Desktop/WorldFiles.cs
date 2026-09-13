using System.IO.Compression;
using System.Management;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PZLauncher.Desktop;

internal static class WorldFiles
{
    internal static string Within(string root, string relative)
    {
        string basis = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(basis, relative));
        if (!path.StartsWith(basis, StringComparison.OrdinalIgnoreCase)) throw new IOException(T("world.path"));
        for (string? current = path; current != null && current.Length >= basis.Length - 1; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(T("world.link", current));
        return path;
    }
    internal static List<string> Enumerate(string root)
    {
        var files = new List<string>(); var pending = new Stack<string>(); pending.Push(Path.GetFullPath(root));
        while (pending.TryPop(out var directory))
        {
            foreach (string item in Directory.EnumerateFileSystemEntries(directory))
            {
                string path = Within(root, Path.GetRelativePath(root, item));
                if (Directory.Exists(path)) pending.Push(path); else files.Add(path);
            }
        }
        return files;
    }
    internal static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    internal static string Backup(string root, string backupRoot)
    {
        string source = Path.GetFullPath(root).TrimEnd('\\', '/');
        string destination = Path.GetFullPath(backupRoot).TrimEnd('\\', '/');
        if (destination.Equals(source, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(T("world.backupInside"));
        var files = Enumerate(source); Directory.CreateDirectory(destination);
        string archive = Path.Combine(destination, Path.GetFileName(source) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        string temp = archive + ".partial";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                foreach (string file in files)
                {
                    
                    if (file.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".db-journal", StringComparison.OrdinalIgnoreCase)) continue;
                    string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                    if (file.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                    {
                        string snapshot = Path.Combine(destination, Guid.NewGuid().ToString("N") + ".db");
                        try { DatabaseReader.Snapshot(file, snapshot); zip.CreateEntryFromFile(snapshot, relative, CompressionLevel.Fastest); }
                        finally { if (File.Exists(snapshot)) File.Delete(snapshot); }
                    }
                    else zip.CreateEntryFromFile(file, relative, CompressionLevel.Fastest);
                }
            }
            File.Move(temp, archive); return archive;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static string RestoreAsCopy(string archive, string parent)
    {
        using var zip = ZipFile.OpenRead(archive);
        string destination = Path.Combine(parent, "restored-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            string target = Within(destination, entry.FullName);
            if (!names.Add(target) || Path.IsPathRooted(entry.FullName) || entry.FullName.Contains(':')) throw new IOException(T("world.path"));
        }
        Directory.CreateDirectory(destination);
        foreach (var entry in zip.Entries)
        {
            string target = Within(destination, entry.FullName);
            if (entry.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target, false);
        }
        return destination;
    }
}
internal sealed record ChunkFile(string Path, string Layer, int X, int Y, bool Cell);
internal sealed record WipePlan(string Root, Rectangle Cells, bool Outside, bool Vehicles, List<ChunkFile> Files, Dictionary<string,string> Hashes, int VehicleCount);
internal static class ChunkWipe
{
    internal const int ChunksPerCell = 32;
    internal static List<ChunkFile> Scan(string root)
    {
        var result = new List<ChunkFile>();
        foreach (string file in WorldFiles.Enumerate(root))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var chunk = Regex.Match(relative, @"^(map|blam)/(-?\d+)/(-?\d+)\.bin$");
            if (chunk.Success) { result.Add(new(file, chunk.Groups[1].Value, int.Parse(chunk.Groups[2].Value), int.Parse(chunk.Groups[3].Value), false)); continue; }
            var region = Regex.Match(relative, @"^isoregiondata/datachunk_(-?\d+)_(-?\d+)\.bin$");
            if (region.Success) { result.Add(new(file, "isoregiondata", int.Parse(region.Groups[1].Value), int.Parse(region.Groups[2].Value), false)); continue; }
            var cell = Regex.Match(relative, @"^(?:(chunkdata|zpop|apop|metagrid)/)?(chunkdata|zpop|apop|metacell)_(-?\d+)_(-?\d+)\.bin$");
            if (cell.Success && (!cell.Groups[1].Success || cell.Groups[1].Value == (cell.Groups[2].Value == "metacell" ? "metagrid" : cell.Groups[2].Value)))
                result.Add(new(file, cell.Groups[2].Value, int.Parse(cell.Groups[3].Value), int.Parse(cell.Groups[4].Value), true));
        }
        return result;
    }
    internal static WipePlan Prepare(string root, Rectangle cells, bool outside, bool zombies, bool animals, bool vehicles)
    {
        if (!Directory.Exists(Path.Combine(root, "map"))) throw new IOException(T("wipe.format"));
        if (cells.Width <= 0 || cells.Height <= 0 || cells.Left < -10000 || cells.Top < -10000 || cells.Right > 10000 || cells.Bottom > 10000)
            throw new IOException(T("wipe.selection"));
        bool Selected(ChunkFile file)
        {
            if (file.Layer == "zpop" && !zombies || file.Layer == "apop" && !animals) return false;
            int x = file.Cell ? file.X : (int)Math.Floor(file.X / 32.0), y = file.Cell ? file.Y : (int)Math.Floor(file.Y / 32.0);
            return cells.Contains(x, y) != outside;
        }
        var files = Scan(root).Where(Selected).ToList();
        var hashes = files.ToDictionary(f => f.Path, f => WorldFiles.Hash(f.Path));
        string db = Path.Combine(root, "vehicles.db"); int count = 0;
        if (vehicles && File.Exists(db))
        {
            using var connection = DatabaseReader.Open(db); using var command = VehicleCommand(connection, cells, outside, false);
            count = Convert.ToInt32(command.ExecuteScalar());
            hashes[db] = WorldFiles.Hash(db);
            foreach (string suffix in new[] { "-wal", "-shm" }) if (File.Exists(db + suffix)) hashes[db + suffix] = WorldFiles.Hash(db + suffix);
        }
        return new(root, cells, outside, vehicles, files, hashes, count);
    }
    private static SqliteCommand VehicleCommand(SqliteConnection connection, Rectangle cells, bool outside, bool delete)
    {
        var command = connection.CreateCommand();
        command.CommandText = (delete ? "DELETE FROM vehicles WHERE " : "SELECT count(*) FROM vehicles WHERE ") + (outside ? "NOT " : "") + "(wx >= $x1 AND wx < $x2 AND wy >= $y1 AND wy < $y2)";
        command.Parameters.AddWithValue("$x1", cells.Left * ChunksPerCell); command.Parameters.AddWithValue("$x2", cells.Right * ChunksPerCell);
        command.Parameters.AddWithValue("$y1", cells.Top * ChunksPerCell); command.Parameters.AddWithValue("$y2", cells.Bottom * ChunksPerCell); return command;
    }
    internal static string Execute(WipePlan plan, string backupRoot, Action ensureStopped)
    {
        ensureStopped();
        foreach (var (path, hash) in plan.Hashes)
        {
            WorldFiles.Within(plan.Root, Path.GetRelativePath(plan.Root, path));
            if (!File.Exists(path) || WorldFiles.Hash(path) != hash) throw new IOException(T("world.changed"));
        }
        string backup = WorldFiles.Backup(plan.Root, backupRoot); ensureStopped();
        foreach (var (path, hash) in plan.Hashes.Where(h => !h.Key.EndsWith("-shm")))
            if (WorldFiles.Hash(path) != hash) throw new IOException(T("world.changed"));
        string quarantine = backup + ".wiped";
        var moved = new List<(string From, string To)>();
        string vehicles = Path.Combine(plan.Root, "vehicles.db");
        using var connection = plan.Vehicles && File.Exists(vehicles) ? DatabaseReader.Open(vehicles, false) : null;
        using var transaction = connection?.BeginTransaction();
        try
        {
            if (connection != null)
            {
                using var command = VehicleCommand(connection, plan.Cells, plan.Outside, true); command.Transaction = transaction;
                if (command.ExecuteNonQuery() != plan.VehicleCount) throw new IOException(T("world.changed"));
            }
            foreach (var file in plan.Files)
            {
                string source = WorldFiles.Within(plan.Root, Path.GetRelativePath(plan.Root, file.Path));
                if (WorldFiles.Hash(source) != plan.Hashes[file.Path]) throw new IOException(T("world.changed"));
                string target = WorldFiles.Within(quarantine, Path.GetRelativePath(plan.Root, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Move(source, target); moved.Add((source, target));
            }
            transaction?.Commit();
        }
        catch
        {
            transaction?.Rollback(); foreach (var item in moved.AsEnumerable().Reverse()) File.Move(item.To, item.From); throw;
        }
        LauncherStorage.WriteAtomic(backup + ".json", JsonSerializer.Serialize(new { plan.Cells, plan.Outside, plan.VehicleCount, files = plan.Files.Select(f => Path.GetRelativePath(plan.Root, f.Path)), backup, quarantine }));
        return backup;
    }
}
internal static class GameProcessGuard
{
    internal static void EnsureStopped(string cache)
    {
        using var query = new ManagementObjectSearcher("SELECT Name, CommandLine FROM Win32_Process WHERE Name='java.exe' OR Name='javaw.exe' OR Name='ProjectZomboid64.exe' OR Name='ProjectZomboid32.exe'");
        foreach (ManagementObject process in query.Get())
        {
            string name = Convert.ToString(process["Name"]) ?? "", command = Convert.ToString(process["CommandLine"]) ?? "";
            if (!name.StartsWith("ProjectZomboid", StringComparison.OrdinalIgnoreCase) && !command.Contains("zombie.network.GameServer") &&
                !command.Contains("zombie.gameStates.MainScreenState") && !command.Contains("dev.aoqia.leaf.loader")) continue;
            var match = Regex.Match(command, "(?:\"-cachedir=([^\"]+)\"|-cachedir=\"([^\"]+)\"|-cachedir=([^\\s]+))");
            string runningCache = match.Success ? match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value : LauncherStorage.UserGameData;
            if (Path.GetFullPath(runningCache).TrimEnd('\\', '/').Equals(Path.GetFullPath(cache).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new IOException(T("world.running"));
        }
    }
}
