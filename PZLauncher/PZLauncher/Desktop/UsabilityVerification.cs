using System.Buffers.Binary;
using System.Text;

namespace PZLauncher.Desktop;

internal static class UsabilityVerification
{
    internal static void CreateWorldFiles(string world)
    {
        Directory.CreateDirectory(world);
        byte[] version = new byte[8 + 26]; BinaryPrimitives.WriteInt32BigEndian(version, 249); BinaryPrimitives.WriteInt32BigEndian(version.AsSpan(4), 13);
        Encoding.BigEndianUnicode.GetBytes("Muldraugh, KY").CopyTo(version, 8); File.WriteAllBytes(Path.Combine(world, "map_ver.bin"), version);
        byte[] time = new byte[100]; "GMTM"u8.CopyTo(time); BinaryPrimitives.WriteInt32BigEndian(time.AsSpan(4), 249);
        int[] values = [BitConverter.SingleToInt32Bits(1), 2, 750, BitConverter.SingleToInt32Bits(9), BitConverter.SingleToInt32Bits(10), 8, 6, 1993];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt32BigEndian(time.AsSpan(8 + i * 4), values[i]);
        for (int i = 40; i < time.Length; i++) time[i] = (byte)i;
        File.WriteAllBytes(Path.Combine(world, "map_t.bin"), time);
        using var stream = new MemoryStream(); stream.Write("SAND"u8);
        foreach (int n in new[] { 249, 6, 2 }) { byte[] b = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, n); stream.Write(b); }
        foreach (string s in new[] { "Zombies", "4", "ExampleMod.Text", "éà中" }) { byte[] b = Encoding.UTF8.GetBytes(s); byte[] length = new byte[2]; BinaryPrimitives.WriteInt16BigEndian(length, (short)b.Length); stream.Write(length); stream.Write(b); }
        stream.Write(new byte[] { 0, 4, 84, 69, 83, 84 }); File.WriteAllBytes(Path.Combine(world, "map_sand.bin"), stream.ToArray());
        File.WriteAllText(Path.Combine(world, "mods.txt"), "VERSION = 1,\nmods {\n mod = ExampleMod,\n}\nmaps {\n map = ExampleMap,\n}\n");
    }
    internal static void Run(string root, Action<string, Action> check)
    {
        void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        void Reject(Action action) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Assert(rejected, "Invalid data accepted"); }
        string fixture = Path.Combine(root, "save-editing"); CreateWorldFiles(fixture);
        check("Éditeur : aller-retour exact des quatre fichiers, UTF-16 et UTF-8", () =>
        {
            foreach (string file in Directory.GetFiles(fixture)) { byte[] bytes = File.ReadAllBytes(file); var document = new WorldSettingsDocument(Path.GetFileName(file), bytes); Assert(bytes.SequenceEqual(document.Encode()), file); }
            var map = new WorldSettingsDocument("map_ver.bin", File.ReadAllBytes(Path.Combine(fixture, "map_ver.bin"))); map.Values["Map"] = "Ville éà;地图";
            Assert(new WorldSettingsDocument("map_ver.bin", map.Encode()).Values["Map"] == "Ville éà;地图", "UTF16 mapping");
        });
        check("Éditeur : bloc du temps, calendrier et données inconnues préservées", () =>
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(fixture, "map_t.bin")); var time = new WorldSettingsDocument("map_t.bin", bytes);
            time.Values["TimeOfDay"] = "15.5"; byte[] after = time.Encode();
            Assert(after.AsSpan(28).SequenceEqual(bytes.AsSpan(28)) && after.AsSpan(0, 24).SequenceEqual(bytes.AsSpan(0, 24)), "Other game-time fields modified");
            Assert(new WorldSettingsDocument("map_t.bin", after).Values["TimeOfDay"] == "15.5", "Time did not roundtrip");
            time.Values["Month"] = "2"; time.Values["Day"] = "31"; Reject(() => time.Encode());
            bytes[7] = 250; Reject(() => new WorldSettingsDocument("map_t.bin", bytes));
        });
        check("Éditeur : options de mods et fin du sandbox conservées, liste de cartes conservée", () =>
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(fixture, "map_sand.bin")); var sandbox = new WorldSettingsDocument("map_sand.bin", bytes);
            sandbox.Values["Zombies"] = "6"; byte[] after = sandbox.Encode(); var loaded = new WorldSettingsDocument("map_sand.bin", after);
            Assert(loaded.Values["ExampleMod.Text"] == "éà中" && after.AsSpan(after.Length - 6).SequenceEqual(bytes.AsSpan(bytes.Length - 6)), "Unknown sandbox data lost");
            Reject(() => new WorldSettingsDocument("map_sand.bin", bytes[..20]));
            var mods = new WorldSettingsDocument("mods.txt", File.ReadAllBytes(Path.Combine(fixture, "mods.txt"))); mods.Values["mods"] = "B;A";
            var reread = new WorldSettingsDocument("mods.txt", mods.Encode()); Assert(reread.Values["mods"] == "B;A" && reread.Values["maps"] == "ExampleMap", "Map list or order lost");
        });
        check("Éditeur : sauvegarde atomique, conflit externe et contrôle du processus", () =>
        {
            var original = Directory.GetFiles(fixture).ToDictionary(p => Path.GetFileName(p), File.ReadAllBytes);
            var edited = original.ToDictionary(p => p.Key, p => p.Value.ToArray()); var doc = new WorldSettingsDocument("map_ver.bin", edited["map_ver.bin"]); doc.Values["Map"] = "NewMap"; edited["map_ver.bin"] = doc.Encode();
            bool guarded = false; WorldSettingsDocument.Save(fixture, original, edited, () => guarded = true);
            Assert(guarded && File.Exists(Path.Combine(fixture, "map_ver.bin.pzlauncher.bak")), "No guard or backup");
            byte[] saved = File.ReadAllBytes(Path.Combine(fixture, "map_ver.bin")); File.AppendAllText(Path.Combine(fixture, "mods.txt"), "changed");
            bool rejected = false; try { WorldSettingsDocument.Save(fixture, original, edited, () => { }); } catch (IOException) { rejected = true; }
            Assert(rejected && File.ReadAllBytes(Path.Combine(fixture, "map_ver.bin")).SequenceEqual(saved), "Conflict changed another file");
            rejected = false; try { WorldSettingsDocument.Delete(root, root); } catch (IOException) { rejected = true; } Assert(rejected, "Deletion outside Saves accepted");
        });
        check("Lua : édition ciblée, commentaires, tables imbriquées, choix booléens et expressions refusées", () =>
        {
            const string source = "-- preserved\nSandboxVars = { VERSION=6, Foo=\"true\", ZombieLore={ Speed=2 }, Flag=true, -- comment\n Number=-1.2e2, }\n";
            var doc = new LuaConfigDocument(source); Assert(doc.Values["Foo"].Type == "string" && doc.Values["Number"].Value == "-1.2e2", "Literal types");
            string edited = doc.Set("ZombieLore.Speed", "3", "enum"); Assert(edited == source.Replace("Speed=2", "Speed=3"), "Unrelated Lua altered");
            edited = new LuaConfigDocument(edited).Set("ZombieConfig.NewValue", "quoted\"é", "string");
            var read = new LuaConfigDocument(edited); Assert(read.Values["ZombieConfig.NewValue"].Value == "quoted\"é" && edited.Contains("-- comment"), "Nested insertion");
            var spawn = new LuaConfigDocument("function SpawnRegions() return { { name=\"Rosewood\", file=\"media/a.lua\" }, } end");
            Assert(new LuaConfigDocument(spawn.Set("[1].name", "Riverside", "string")).Values["[1].name"].Value == "Riverside", "Spawn table");
            Reject(() => new LuaConfigDocument("SandboxVars={ Foo=os.execute('anything') }"));
        });
        check("Diagnostics : chaque répétition conserve sa ligne de navigation", () =>
        {
            var issues = ConsoleDiagnostics.Parse("ERROR: General, 0> t:123> Broken\n\nLOG: General, 0> fine\nERROR: General, 0> t:456> Broken\n\n", []);
            Assert(issues.Count == 1 && issues[0].Count == 2 && issues[0].Lines.SequenceEqual(new[] { 1, 4 }), "Repeated error marker lost");
        });
        check("Configuration : types et bornes validés sans coercition silencieuse", () =>
        {
            var integer = new ConfigOption { Name = "MaxPlayers", Type = "integer", Min = 1, Max = 254 }; integer.Validate("32"); Reject(() => integer.Validate("255")); Reject(() => integer.Validate("1.5"));
            var boolean = new ConfigOption { Name = "PVP", Type = "boolean" }; boolean.Validate("false"); Reject(() => boolean.Validate("yes"));
            var number = new ConfigOption { Name = "Test", Type = "double" }; Reject(() => number.Validate("NaN"));
        });
        check("Éditeur visuel : cases, menus et valeurs invalides avant enregistrement", () =>
        {
            var updates = new Dictionary<string, string>();
            using var fields = new ConfigurationFields([
                new() { Name = "PVP", Type = "boolean", Default = "true" },
                new() { Name = "Zombies", Type = "enum", Default = "1", Choices = [new("1", "One"), new("2", "Two")] },
                new() { Name = "MaxPlayers", Type = "integer", Default = "32", Min = 1, Max = 254 }
            ], [], (k, v, _) => updates[k] = v);
            var grid = fields.Controls.OfType<DataGridView>().Single();
            Assert(updates.Count == 0 && grid.Rows[0].Cells[1] is DataGridViewCheckBoxCell && grid.Rows[1].Cells[1] is DataGridViewComboBoxCell, "Editor wrote defaults or missing typed cells");
            grid.Rows[0].Cells[1].Value = false; grid.Rows[1].Cells[1].Value = "2"; fields.Commit();
            Assert(updates["PVP"] == "false" && updates["Zombies"] == "2", "UI change not applied");
            grid.Rows[2].Cells[1].Value = "999"; Reject(() => fields.Commit()); Assert(!updates.ContainsKey("MaxPlayers"), "Invalid UI value written");
        });
        check("Mods : copie locale RadarZ conservée et dossiers ignorés expliqués", () =>
        {
            var profile = new PlayerProfile { CachePath = Path.Combine(root, "mod-copies"), Steam = true }; profile.Launch.ModFolders = "mods";
            string mod = Path.Combine(profile.CachePath, "mods", "RadarZ", "42.0"); Directory.CreateDirectory(mod);
            File.WriteAllText(Path.Combine(mod, "mod.info"), "id=radarz\nname=RadarZ\nversionMin=42.0.0\n");
            Directory.CreateDirectory(Path.Combine(profile.CachePath, "mods", "MissingInfo"));
            var issues = new List<string>(); var found = ModCatalog.Scan(profile, new Version(42, 20), issues).Single(m => m.Id == "radarz");
            Assert(found.Available && found.Copies.Any(c => c.Source == "Local") && issues.Any(i => i.Contains("MissingInfo")), "Local version or ignored folder diagnosis");
        });
        string installation = LauncherStorage.Load().Installation;
        if (File.Exists(Path.Combine(installation, "projectzomboid.jar")))
            check("Installation réelle : registre des options, types, choix et réglages absents du fichier", () =>
            {
                var schema = Task.Run(() => GameConfigurationSchema.Load(installation)).GetAwaiter().GetResult();
                Assert(schema.Server.Count > 100 && schema.Sandbox.Count > 200 && schema.SandboxVersion > 0, "Missing registry entries");
                Assert(schema.Server.Single(o => o.Name == "PVP").Type == "boolean" && schema.Sandbox.Single(o => o.Name == "Zombies").Choices.Count > 0, "Missing typed choices");
                var defaults = ServerFiles.IniValues("Public=false\n"); Assert(schema.Server.Any(o => !defaults.ContainsKey(o.Name)), "No missing registered options");
                File.WriteAllText(Path.Combine(LauncherStorage.Root, "configuration-schema-verification.json"), System.Text.Json.JsonSerializer.Serialize(new { schema.Server.Count, SandboxCount = schema.Sandbox.Count, schema.SandboxVersion }));
            });
        var active = LauncherStorage.Load().Profiles.FirstOrDefault();
        if (active != null && Directory.Exists(Path.Combine(active.CachePath, "mods", "RadarZ")))
            check("RadarZ réel : copie locale visible et priorité Workshop conservée", () =>
            {
                var found = ModCatalog.Scan(active, InstallationLocator.ReadGameVersion(installation)).Single(m => m.Id == "radarz");
                Assert(found.Copies.Any(c => c.Source == "Local" && c.Location.EndsWith("RadarZ", StringComparison.OrdinalIgnoreCase)), "RadarZ local hidden");
                var order = active.Launch.ModFolders.Split(',');
                var expected = found.Copies.Where(c => c.Available).OrderBy(c => Array.IndexOf(order, c.Source == "Workshop" ? "steam" : c.Source == "Staged" ? "workshop" : "mods")).First();
                Assert(found.Location == expected.Location, "Local visibility changed actual priority");
                File.WriteAllText(Path.Combine(LauncherStorage.Root, "radarz-detection-verification.json"), System.Text.Json.JsonSerializer.Serialize(new { found.Id, found.Source, found.Location, found.Copies }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            });
        if (active != null && Directory.Exists(Path.Combine(active.CachePath, "Server")))
            check("Fichiers serveur réels : lecture Lua sans exécution et aller-retour littéral", () =>
            {
                foreach (string file in Directory.GetFiles(Path.Combine(active.CachePath, "Server"), "*.lua"))
                {
                    string text = File.ReadAllText(file); var doc = new LuaConfigDocument(text);
                    foreach (var value in doc.Values.Values.Take(1)) Assert(new LuaConfigDocument(doc.Set(value.Name, value.Value, value.Type)).Values[value.Name].Value == value.Value, "Lua literal roundtrip: " + Path.GetFileName(file));
                    Assert(File.ReadAllText(file) == text, "User configuration changed");
                }
            });
        string? realWorld = active == null ? null : SaveCatalog.Scan(active.CachePath).OrderByDescending(s => s.Modified).FirstOrDefault()?.Path;
        if (realWorld != null && new[] { "mods.txt", "map_ver.bin", "map_t.bin", "map_sand.bin" }.All(n => File.Exists(Path.Combine(realWorld, n))))
            check("Sauvegarde réelle : lecture et aller-retour en mémoire sans écriture utilisateur", () =>
            {
                foreach (string name in new[] { "mods.txt", "map_ver.bin", "map_t.bin", "map_sand.bin" })
                {
                    byte[] bytes = File.ReadAllBytes(Path.Combine(realWorld, name)); var document = new WorldSettingsDocument(name, bytes);
                    Assert(document.Encode().SequenceEqual(bytes), "Real file changed on roundtrip: " + name);
                    string key = name switch { "mods.txt" => "mods", "map_ver.bin" => "Map", "map_t.bin" => "TimeOfDay", _ => "Zombies" };
                    string value = name is "mods.txt" or "map_ver.bin" ? document.Values[key] + ";VerificationOnly" : name == "map_t.bin" ? "12.5" : "6";
                    document.Values[key] = value; byte[] changed = document.Encode();
                    Assert(new WorldSettingsDocument(name, changed).Values[key] == value, "Real file edit failed: " + name);
                    if (name == "map_t.bin") Assert(changed.AsSpan(28).SequenceEqual(bytes.AsSpan(28)), "Real time tail altered");
                    Assert(File.ReadAllBytes(Path.Combine(realWorld, name)).SequenceEqual(bytes), "Real world modified");
                }
            });
    }
}
