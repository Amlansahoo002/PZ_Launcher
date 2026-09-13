using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PZLauncher.Desktop;

internal static class FtpWorldVerification
{
    private sealed class Faults { internal int Moves; internal int FailOnMove; }
    private sealed class LocalConnection(string root, Faults faults) : IWorldConnection
    {
        private string FilePath(string relative) => WorldFiles.Within(root, FtpWorldConnection.Relative(relative));
        public Task<List<RemoteWorldEntry>> List(string relative, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); string path = relative.Length == 0 ? root : FilePath(relative);
            return Task.FromResult(Directory.EnumerateFileSystemEntries(path).Select(p => new RemoteWorldEntry(Path.GetFileName(p), Directory.Exists(p), File.Exists(p) ? new FileInfo(p).Length : 0)).ToList());
        }
        public Task Download(string relative, string local, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.GetDirectoryName(local)!); File.Copy(FilePath(relative), local, true); return Task.CompletedTask; }
        public Task Upload(string local, string relative, CancellationToken token)
        { token.ThrowIfCancellationRequested(); File.Copy(local, FilePath(relative), false); return Task.CompletedTask; }
        public Task Move(string from, string to, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (++faults.Moves == faults.FailOnMove) throw new IOException("Injected rename failure"); File.Move(FilePath(from), FilePath(to)); return Task.CompletedTask; }
        public Task<bool> Exists(string relative, CancellationToken token) => Task.FromResult(File.Exists(FilePath(relative)));
        public Task Delete(string relative, CancellationToken token) { File.Delete(FilePath(relative)); return Task.CompletedTask; }
        public void Dispose() { }
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Await(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();
    internal static void Run(string root, Action<string, Action> check)
    {
        int index = 0;
        var savedSettings = new FtpWorldSettings { Host = "fixture", User = "test", Password = "never-persist-secret", Directory = "/world" };
        (FtpWorldWorkspace Workspace, string Remote, Faults Faults) Fixture(bool chunks = true)
        {
            string basis = Path.Combine(root, "ftp-tests", (++index).ToString());
            string remote = ManagementVerification.CreateWorld(Path.Combine(basis, "remote")); UsabilityVerification.CreateWorldFiles(remote);
            File.WriteAllText(Path.Combine(remote, "unrelated.txt"), "keep");
            var faults = new Faults(); var workspace = new FtpWorldWorkspace(Path.Combine(basis, "local"), "fixture/world", chunks, () => new LocalConnection(remote, faults), savedSettings);
            Await(() => workspace.Download(null, CancellationToken.None)); return (workspace, remote, faults);
        }
        check("FTP : copie éditeur, fichiers non suivis et chemins invalides", () =>
        {
            var f = Fixture(false);
            Assert(!Directory.Exists(Path.Combine(f.Workspace.LocalRoot, "map")), "Editor copy downloaded map chunks");
            Assert(FtpWorldWorkspace.EditorFiles.All(n => File.Exists(Path.Combine(f.Workspace.LocalRoot, n))), "Editor files absent");
            File.WriteAllText(Path.Combine(f.Workspace.LocalRoot, "mods.txt.pzlauncher.bak"), "backup");
            Assert(f.Workspace.Changes().Count == 0, "Local backup scheduled for upload");
            foreach (string path in new[] { "../escape", "/absolute", "map/../../escape", "x:y", "x\\y", "map/CON.bin", "a/../b", "bad\r\nname", "file. " })
            {
                bool rejected = false; try { FtpWorldConnection.Relative(path); } catch (IOException) { rejected = true; }
                Assert(rejected, "Invalid remote path accepted: " + path);
            }
        });
        check("FTP : édition binaire, wipe, véhicules, remplacement et originaux conservés", () =>
        {
            var f = Fixture(); string local = f.Workspace.LocalRoot;
            var time = new WorldSettingsDocument("map_t.bin", File.ReadAllBytes(Path.Combine(local, "map_t.bin")));
            time.Values["TimeOfDay"] = "17.25"; File.WriteAllBytes(Path.Combine(local, "map_t.bin"), time.Encode());
            var plan = ChunkWipe.Prepare(local, new Rectangle(0, 0, 1, 1), false, true, true, true);
            string deleted = Path.GetRelativePath(local, plan.Files[0].Path);
            ChunkWipe.Execute(plan, Path.Combine(root, "ftp-wipe-backups"), () => { });
            var changes = f.Workspace.Changes(); Assert(changes.Any(c => c.Deleted) && changes.Any(c => c.Relative == "vehicles.db"), "Wipe changes absent");
            Await(() => f.Workspace.Apply(changes, null, CancellationToken.None));
            Assert(!File.Exists(Path.Combine(f.Remote, deleted)), "Remote chunk still exists");
            Assert(new WorldSettingsDocument("map_t.bin", File.ReadAllBytes(Path.Combine(f.Remote, "map_t.bin"))).Values["TimeOfDay"] == "17.25", "Remote world edit missing");
            using var db = DatabaseReader.Open(Path.Combine(f.Remote, "vehicles.db")); using var count = db.CreateCommand(); count.CommandText = "SELECT count(*) FROM vehicles WHERE wx >= 0 AND wx < 32 AND wy >= 0 AND wy < 32";
            Assert(Convert.ToInt32(count.ExecuteScalar()) == 0, "Remote vehicle DB not replaced");
            Assert(File.Exists(Path.Combine(f.Remote, "unrelated.txt")) && Directory.GetFiles(f.Remote, "*.bak", SearchOption.AllDirectories).Length == changes.Count, "Original backups/unrelated files lost");
            Assert(f.Workspace.Changes().Count == 0, "Successful transaction baseline not advanced");
        });
        check("FTP : conflit distant refusé avant toute mutation", () =>
        {
            var f = Fixture(false); File.AppendAllText(Path.Combine(f.Workspace.LocalRoot, "mods.txt"), "local");
            File.AppendAllText(Path.Combine(f.Remote, "mods.txt"), "remote"); var changes = f.Workspace.Changes();
            bool rejected = false; try { Await(() => f.Workspace.Apply(changes, null, CancellationToken.None)); } catch (IOException) { rejected = true; }
            Assert(rejected && f.Faults.Moves == 0 && File.ReadAllText(Path.Combine(f.Remote, "mods.txt")).EndsWith("remote"), "Remote conflict overwritten");
        });
        check("FTP : échec de remplacement, rollback et nouveau téléchargement requis", () =>
        {
            var f = Fixture(false); string original = WorldFiles.Hash(Path.Combine(f.Remote, "mods.txt"));
            File.AppendAllText(Path.Combine(f.Workspace.LocalRoot, "mods.txt"), "local"); f.Faults.FailOnMove = 2;
            bool rejected = false; try { Await(() => f.Workspace.Apply(f.Workspace.Changes(), null, CancellationToken.None)); } catch (IOException) { rejected = true; }
            Assert(rejected && f.Workspace.RequiresReload && WorldFiles.Hash(Path.Combine(f.Remote, "mods.txt")) == original, "Failed commit did not restore original");
        });
        check("FTP : WAL distant et annulation n’appliquent aucune modification", () =>
        {
            var f = Fixture(false); File.AppendAllText(Path.Combine(f.Workspace.LocalRoot, "mods.txt"), "local");
            File.WriteAllText(Path.Combine(f.Remote, "vehicles.db-wal"), "uncheckpointed"); bool rejected = false;
            try { Await(() => f.Workspace.Apply(f.Workspace.Changes(), null, CancellationToken.None)); } catch (IOException) { rejected = true; }
            Assert(rejected && f.Faults.Moves == 0, "Remote WAL not rejected"); File.Delete(Path.Combine(f.Remote, "vehicles.db-wal"));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); rejected = false;
            try { Await(() => f.Workspace.Apply(f.Workspace.Changes(), null, cancelled.Token)); } catch (OperationCanceledException) { rejected = true; }
            Assert(rejected && f.Faults.Moves == 0, "Cancelled transfer mutated remote");
        });
        check("FTP : édition SQLite locale en WAL refusée avant comparaison", () =>
        {
            var f = Fixture(false);
            using var writer = new SqliteConnection("Data Source=" + Path.Combine(f.Workspace.LocalRoot, "vehicles.db") + ";Pooling=False"); writer.Open();
            using var command = writer.CreateCommand(); command.CommandText = "PRAGMA journal_mode=WAL; DELETE FROM vehicles;"; command.ExecuteNonQuery();
            bool rejected = false; try { f.Workspace.Changes(); } catch (IOException) { rejected = true; }
            Assert(rejected && f.Faults.Moves == 0, "Local WAL changes ignored");
        });
        check("FTP : reprise des changements sans stocker le mot de passe", () =>
        {
            var f = Fixture(false); File.AppendAllText(Path.Combine(f.Workspace.LocalRoot, "mods.txt"), "local resumed");
            string manifest = Path.Combine(Path.GetDirectoryName(f.Workspace.LocalRoot)!, "manifest.json");
            Assert(!File.ReadAllText(manifest).Contains(savedSettings.Password), "FTP password persisted");
            var resumed = FtpWorldWorkspace.Resume(manifest, savedSettings, () => new LocalConnection(f.Remote, f.Faults));
            Assert(resumed.Changes().Count == 1 && !resumed.IncludesChunks, "Unsent changes lost on resume");
            Await(() => resumed.Apply(resumed.Changes(), null, CancellationToken.None));
            Assert(File.ReadAllText(Path.Combine(f.Remote, "mods.txt")).EndsWith("local resumed"), "Resumed edit not uploaded");
            bool rejected = false; try { FtpWorldWorkspace.Resume(manifest, savedSettings.At("/another")); } catch (IOException) { rejected = true; }
            Assert(rejected, "Resume accepted a different destination");
        });
    }
    
    internal static int Network(string fixture, int port)
    {
        var results = new List<object>(); int failures = 0;
        void Check(string name, Action action)
        {
            try { action(); results.Add(new { name, passed = true }); }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); }
        }
        string remote = ManagementVerification.CreateWorld(Path.Combine(fixture, "server", "Monde été")); UsabilityVerification.CreateWorldFiles(remote);
        var settings = new FtpWorldSettings { Host = "127.0.0.1", Port = port, User = "launcher-test", Password = "launcher-test", Directory = "/Monde été" };
        var workspace = new FtpWorldWorkspace(Path.Combine(fixture, "network-copy"), "loopback/Monde été", true, () => new FtpWorldConnection(settings), settings);
        Check("FTP loopback : listing UTF-8 et téléchargement de la sauvegarde", () =>
        { Await(() => workspace.Download(null, CancellationToken.None)); Assert(ChunkWipe.Scan(workspace.LocalRoot).Count > 0, "Chunks not downloaded"); });
        Check("FTP loopback : édition + wipe + remplacement SQLite + backups", () =>
        {
            string local = workspace.LocalRoot;
            var doc = new WorldSettingsDocument("map_sand.bin", File.ReadAllBytes(Path.Combine(local, "map_sand.bin")));
            doc.Values["Zombies"] = "6"; File.WriteAllBytes(Path.Combine(local, "map_sand.bin"), doc.Encode());
            var plan = ChunkWipe.Prepare(local, new Rectangle(0, 0, 1, 1), false, true, true, true);
            ChunkWipe.Execute(plan, Path.Combine(fixture, "backups"), () => { });
            var changes = workspace.Changes(); Await(() => workspace.Apply(changes, null, CancellationToken.None));
            Assert(new WorldSettingsDocument("map_sand.bin", File.ReadAllBytes(Path.Combine(remote, "map_sand.bin"))).Values["Zombies"] == "6", "Sandbox not uploaded");
            foreach (var c in changes)
                Assert(c.Deleted ? !File.Exists(Path.Combine(remote, c.Relative)) : WorldFiles.Hash(Path.Combine(remote, c.Relative)) == c.UpdatedHash, "Remote mismatch: " + c.Relative);
            Assert(Directory.GetFiles(remote, "*.bak", SearchOption.AllDirectories).Length == changes.Count, "Missing originals");
        });
        Check("FTP loopback : conflit externe détecté avant remplacement", () =>
        {
            File.AppendAllText(Path.Combine(workspace.LocalRoot, "mods.txt"), "local"); File.AppendAllText(Path.Combine(remote, "mods.txt"), "external");
            bool rejected = false; try { Await(() => workspace.Apply(workspace.Changes(), null, CancellationToken.None)); } catch (IOException) { rejected = true; }
            Assert(rejected && File.ReadAllText(Path.Combine(remote, "mods.txt")).EndsWith("external"), "External update overwritten");
        });
        Check("FTP loopback : identifiants incorrects refusés", () =>
        {
            var invalid = new FtpWorldSettings { Host = "127.0.0.1", Port = port, User = "launcher-test", Password = "incorrect" };
            using var connection = new FtpWorldConnection(invalid); bool rejected = false;
            try { Await(async () => await connection.List("", CancellationToken.None)); } catch { rejected = true; }
            Assert(rejected, "Invalid credentials accepted");
        });
        File.WriteAllText(Path.Combine(fixture, "network-results.json"), JsonSerializer.Serialize(new { failures, results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
