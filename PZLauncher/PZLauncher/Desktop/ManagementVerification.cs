using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class ManagementVerification
{
    internal static void Run(string root, Action<string, Action> check)
    {
        void Assert(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        string Fixture(string name) { string directory = Path.Combine(root, name); Directory.CreateDirectory(directory); return directory; }
        check("Wipe : aperçu B42, coordonnées négatives et limites de cellules", () =>
        {
            string world = Fixture("wipe-boundaries");
            foreach (string file in new[] { "map/-1/-1.bin", "map/0/0.bin", "map/31/31.bin", "map/32/32.bin", "zpop/zpop_0_0.bin", "apop/apop_0_0.bin", "notes.txt" })
            { string path = WorldFiles.Within(world, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, file); }
            var inside = ChunkWipe.Prepare(world, new Rectangle(0, 0, 1, 1), false, false, false, false);
            Assert(inside.Files.Count == 2 && inside.Files.All(f => f.X is 0 or 31), "Frontière 31/32 incorrecte.");
            var negative = ChunkWipe.Prepare(world, new Rectangle(-1, -1, 1, 1), false, true, true, false);
            Assert(negative.Files.Count == 1 && negative.Files[0].X == -1, "Division négative incorrecte.");
            var outside = ChunkWipe.Prepare(world, new Rectangle(0, 0, 1, 1), true, true, true, false);
            Assert(outside.Files.Count == 2 && outside.Files.All(f => f.Layer == "map"), "Sélection inversée incorrecte.");
            Assert(File.Exists(Path.Combine(world, "map", "0", "0.bin")), "L'aperçu modifie les fichiers.");
        });
        check("Wipe : backup, véhicules transactionnels et restauration séparée", () =>
        {
            string world = CreateWorld(Fixture("wipe-apply"));
            var plan = ChunkWipe.Prepare(world, new Rectangle(0, 0, 1, 1), false, false, false, true);
            string archive = ChunkWipe.Execute(plan, Fixture("world-backups"), () => { });
            Assert(!File.Exists(Path.Combine(world, "map", "0", "0.bin")) && File.Exists(Path.Combine(world, "map", "32", "0.bin")), "Fichiers ciblés incorrects.");
            Assert(CountVehicles(world) == 1, "Véhicule hors zone touché.");
            string restored = WorldFiles.RestoreAsCopy(archive, Fixture("restored-worlds"));
            Assert(CountVehicles(restored) == 2 && File.Exists(Path.Combine(restored, "map", "0", "0.bin")), "Backup incomplet.");
            Assert(File.ReadAllText(Path.Combine(world, "map_meta.bin")) == "KEEP", "Métadonnées globales touchées.");
        });
        check("Wipe : annulation sur fichier verrouillé et refus d'aperçu périmé", () =>
        {
            string world = CreateWorld(Fixture("wipe-rollback"));
            string second = Path.Combine(world, "map", "0", "1.bin"); File.WriteAllText(second, "second");
            var plan = ChunkWipe.Prepare(world, new Rectangle(0, 0, 1, 1), false, false, false, true);
            bool failed = false;
            using (var locked = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read))
                try { ChunkWipe.Execute(plan, Fixture("rollback-backups"), () => { }); } catch (IOException) { failed = true; }
            Assert(failed && File.Exists(Path.Combine(world, "map", "0", "0.bin")) && CountVehicles(world) == 2, "Rollback incomplet.");
            File.WriteAllText(second, "changed"); failed = false;
            try { ChunkWipe.Execute(plan, Fixture("stale-backups"), () => { }); } catch (IOException) { failed = true; }
            Assert(failed && CountVehicles(world) == 2, "Aperçu périmé accepté.");
        });
        check("Backups : SQLite WAL cohérent, mode lecture seule et traversée ZIP refusée", () =>
        {
            string world = Fixture("db-wal"); string database = Path.Combine(world, "sample.db");
            using var writer = new SqliteConnection("Data Source=" + database + ";Pooling=False"); writer.Open();
            using (var command = writer.CreateCommand()) { command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE sample(id INTEGER, data BLOB); INSERT INTO sample VALUES(1,x'00010203');"; command.ExecuteNonQuery(); }
            string archive = WorldFiles.Backup(world, Fixture("wal-backups"));
            string copy = WorldFiles.RestoreAsCopy(archive, Fixture("wal-restores"));
            Assert(DatabaseReader.Read(Path.Combine(copy, "sample.db"), "sample").Rows.Count == 1, "Pages WAL absentes du backup.");
            using (var read = DatabaseReader.Open(database))
            using (var command = read.CreateCommand())
            {
                command.CommandText = "DELETE FROM sample"; bool failed = false;
                try { command.ExecuteNonQuery(); } catch (SqliteException) { failed = true; } Assert(failed, "Connexion de lecture modifiable.");
            }
            string bad = Path.Combine(Fixture("bad-archive"), "bad.zip");
            using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create)) using (var stream = new StreamWriter(zip.CreateEntry("../escape.txt").Open())) stream.Write("bad");
            bool rejected = false; try { WorldFiles.RestoreAsCopy(bad, Fixture("bad-restore")); } catch (IOException) { rejected = true; }
            Assert(rejected, "Traversée ZIP acceptée.");
        });
        check("Bases : schémas dynamiques, pagination et BLOB brut borné", () =>
        {
            string world = CreateWorld(Fixture("database-read")); string database = Path.Combine(world, "vehicles.db");
            Assert(DatabaseReader.Tables(database).Contains("vehicles"), "Table non trouvée.");
            var data = DatabaseReader.Read(database, "vehicles"); Assert(data.Rows.Count == 2 && data.Rows[0]["data"] is byte[], "BLOB perdu dans la grille.");
            string report = DatabaseReader.Blob(database, "vehicles", "data", new byte[] { 0, 1, 2 }, new Dictionary<string, object>());
            Assert(report.Contains("000102"), "BLOB court mal traité.");
            bool failed = false; try { DatabaseReader.Read(database, "vehicles; DROP TABLE vehicles"); } catch (InvalidDataException) { failed = true; }
            Assert(failed, "Identifiant SQL libre accepté.");
        });
        var localWorld = SaveCatalog.Scan(LauncherStorage.DefaultGameData).FirstOrDefault(s => File.Exists(Path.Combine(s.Path, "players.db")) && File.Exists(Path.Combine(s.Path, "vehicles.db")));
        if (localWorld != null) check("Bases locales : lecture et inspection de BLOBs joueur/véhicule sans modifier les DB", () =>
        {
            int inspected = 0;
            foreach (string name in new[] { "players.db", "vehicles.db" })
            {
                string path = Path.Combine(localWorld.Path, name), before = WorldFiles.Hash(path);
                foreach (string table in DatabaseReader.Tables(path))
                {
                    using var data = DatabaseReader.Read(path, table);
                    var row = data.Rows.Cast<System.Data.DataRow>().FirstOrDefault(r => r.ItemArray.Any(v => v is byte[] bytes && bytes.Length >= 27));
                    if (row == null) continue;
                    var blobColumn = data.Columns.Cast<System.Data.DataColumn>().First(c => row[c] is byte[]);
                    var values = data.Columns.Cast<System.Data.DataColumn>().Where(c => row[c] is not byte[]).ToDictionary(c => c.ColumnName, c => row[c]);
                    string report = DatabaseReader.Blob(path, table, blobColumn.ColumnName, (byte[])row[blobColumn], values);
                    Assert(report.Contains("Byte order: BigEndian") && report.Contains("First bytes:") && !report.StartsWith(T("db.decodeFailed")), "Inspecteur non exécuté sur le BLOB réel.");
                    inspected++; break;
                }
                Assert(WorldFiles.Hash(path) == before, "DB réelle modifiée par la lecture.");
            }
            Assert(inspected == 2, "BLOB joueur ou véhicule absent de la vérification locale.");
        });
        check("Serveur : fichiers conservés, ports validés et commande Steam 0/1", () =>
        {
            var preset = new ServerPreset { Name = "fixture-server", CachePath = Fixture("server-cache") };
            ServerFiles.Create(preset); var original = ServerFiles.Read(preset);
            var edits = new Dictionary<string,string>(original) { [".ini"] = original[".ini"] + "\n# retained\nFutureOption=keep\n", ["_SandboxVars.lua"] = "SandboxVars = { VERSION = 6, Zombies = 4 }\n" };
            ServerFiles.Save(preset, original, edits);
            Assert(File.ReadAllText(preset.Ini).Contains("FutureOption=keep") && File.Exists(preset.Ini + ".pzlauncher.bak"), "Config ou backup perdus.");
            bool bad = false; try { ServerFiles.ValidateIni("DefaultPort=99999"); } catch (IOException) { bad = true; } Assert(bad, "Port invalide accepté.");
            ServerFiles.ValidateIni("DefaultPort=0\nUDPPort=0\nRCONPort=0\nMaxPlayers=254\nBackupsCount=300\nBackupsPeriod=1500\nSaveWorldEveryMinutes=0\nPublic=FALSE\nPVP=1\nBackupsOnStart=0");
            foreach (string invalid in new[] { "DefaultPort=-1", "MaxPlayers=255", "BackupsCount=0", "BackupsCount=301", "BackupsPeriod=-1", "BackupsPeriod=1501", "SaveWorldEveryMinutes=-1", "PVP=yes" })
            {
                bad = false; try { ServerFiles.ValidateIni(invalid); } catch (IOException) { bad = true; }
                Assert(bad, "Valeur hors limites acceptée : " + invalid);
            }
            string installation = InstallationLocator.Find("");
            foreach (bool steam in new[] { true, false })
            {
                preset.Steam = steam; var plan = ServerFiles.Plan(installation, preset, "fixture-secret");
                Assert(plan.Arguments.Contains("zombie.network.GameServer") && !plan.Arguments.Contains("zombie.gameStates.MainScreenState"), "Mauvais main.");
                Assert(plan.Arguments.Contains("-Dzomboid.steam=" + (steam ? "1" : "0")) && !plan.CommandLinePreview.Contains("fixture-secret"), "Mode Steam ou secret incorrect.");
                Assert(plan.Arguments.Count(a => a.StartsWith("-XX:+Use")) == 1, "Collecteur en double.");
            }
            edits[".ini"] += "\nX=1"; File.AppendAllText(preset.Ini, "\nExternal=1"); bad = false;
            try { ServerFiles.Save(preset, original, edits); } catch (IOException) { bad = true; } Assert(bad, "Écriture externe écrasée.");
        });
        check("Serveur : rollback multi-fichiers et restauration complète dans un nouveau cache", () =>
        {
            var preset = new ServerPreset { Name = "roundtrip", CachePath = Fixture("server-roundtrip"), Steam = false, XmsMb = 512, XmxMb = 2048, Collector = "G1" };
            ServerFiles.Create(preset); CreateWorld(preset.World);
            var original = ServerFiles.Read(preset);
            var edits = new Dictionary<string,string>(original) { [".ini"] = original[".ini"] + "\nFutureOption=changed", ["_SandboxVars.lua"] = "SandboxVars = {}" };
            
            string blocked = Path.Combine(preset.ConfigDirectory, preset.Name + "_SandboxVars.lua"); Directory.CreateDirectory(blocked);
            bool failed = false; try { ServerFiles.Save(preset, original, edits); } catch (IOException) { failed = true; } catch (UnauthorizedAccessException) { failed = true; }
            Assert(failed && File.ReadAllText(preset.Ini) == original[".ini"], "INI non restauré après échec du second fichier.");
            Directory.Delete(blocked); 
            ServerFiles.Save(preset, original, edits);
            string archive = ServerFiles.Backup(preset, Fixture("server-archives"));
            var restored = ServerFiles.Restore(archive, Fixture("server-restores"));
            Assert(restored.CachePath != preset.CachePath && !restored.Steam && restored.Collector == "G1" && restored.XmxMb == 2048, "Profil JVM perdu ou cache d'origine utilisé.");
            Assert(ServerFiles.Read(restored)["_SandboxVars.lua"] == edits["_SandboxVars.lua"] && CountVehicles(restored.World) == 2, "Monde ou configuration absent du backup serveur.");
        });
        check("Serveur : console redirigée, save/quit et masquage du secret", () =>
        {
            string directory = Fixture("server-process"); string script = Path.Combine(directory, "fixture.cmd");
            File.WriteAllText(script, "@echo off\necho READY fixture-secret\n:read\nset /p fixtureCommand=\nif \"%fixtureCommand%\"==\"quit\" exit /b 0\nif \"%fixtureCommand%\"==\"save\" echo WORLD_SAVED\ngoto read\n");
            var plan = new LaunchPlan { JavaExecutable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                WorkingDirectory = directory, ConsoleLogPath = Path.Combine(directory, "console.txt"), Arguments = ["/d", "/q", "/c", script] };
            using var session = new ServerSession(plan, new() { Name = "fixture", CachePath = directory }, "fixture-secret");
            bool Wait(Func<bool> predicate) { var timer = Stopwatch.StartNew(); while (!predicate() && timer.ElapsedMilliseconds < 5000) Thread.Sleep(20); return predicate(); }
            Assert(Wait(() => session.Output.Contains("READY")) && !session.Output.Contains("fixture-secret"), "Sortie ou masquage absent.");
            session.Command("save"); Assert(Wait(() => session.Output.Contains("WORLD_SAVED")), "Commande save perdue.");
            session.Command("quit"); Assert(Wait(() => !session.Running), "Commande quit perdue.");
        });
        check("Logs : traces multilignes, répétitions et attribution prudente", () =>
        {
            const string error = "ERROR: General, 0> t:1234567890123> java.lang.NoSuchMethodError: sample.Call\n at sample.Main.run(Main.java:4)\n /mods/MyMod/media/lua/test.lua\n";
            var result = ConsoleDiagnostics.Parse(error + "LOG : General, 0> t:1234567890124> ready\n" + error.Replace("1234567890123", "1234567890999"), []);
            Assert(result.Count == 1 && result[0].Count == 2 && result[0].Evidence.Contains("Main.java:4") && result[0].Attribution.Contains("MyMod"), "Pile ou regroupement perdus.");
            var oom = ConsoleDiagnostics.Parse("java.lang.OutOfMemoryError: Direct buffer memory\n", []);
            Assert(oom.Count == 1 && oom[0].Explanation == T("log.nativeMemory"), "Mémoire native confondue avec le tas.");
            Assert(ConsoleDiagnostics.Parse("java.lang.OutOfMemoryError: unable to create native thread\n", [])[0].Explanation == T("log.nativeMemory"), "Thread natif attribué au tas.");
            Assert(ConsoleDiagnostics.Parse("warn : General, 0> t:123> notice\n", [])[0].Level == "WARN", "Niveau insensible à la casse incorrect.");
            Assert(ConsoleDiagnostics.Parse("LOG : General, 0> t:1234567890123> Loaded mod MyMod\n", []).Count == 0, "Mod chargé pris pour une erreur.");
        });
    }
    internal static string CreateWorld(string root)
    {
        foreach (string path in new[] { "map/0/0.bin", "map/32/0.bin", "map_meta.bin" })
        { string file = WorldFiles.Within(root, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "KEEP"); }
        using var db = new SqliteConnection("Data Source=" + Path.Combine(root, "vehicles.db") + ";Pooling=False"); db.Open();
        using var command = db.CreateCommand(); command.CommandText = "CREATE TABLE vehicles(id INTEGER PRIMARY KEY, wx INTEGER, wy INTEGER, x REAL, y REAL, worldversion INTEGER, data BLOB); INSERT INTO vehicles VALUES(1,0,0,1,2,249,x'010203'),(2,32,0,257,2,249,x'040506');"; command.ExecuteNonQuery(); return root;
    }
    private static int CountVehicles(string root) { using var db = DatabaseReader.Open(Path.Combine(root, "vehicles.db")); using var c = db.CreateCommand(); c.CommandText = "SELECT count(*) FROM vehicles"; return Convert.ToInt32(c.ExecuteScalar()); }
}
