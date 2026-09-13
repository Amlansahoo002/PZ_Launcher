using System.Runtime.InteropServices;
using System.Text.Json;

namespace PZLauncher.Desktop;

internal static class ConfigurationBooleanVerification
{
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    internal static int Smoke()
    {
        string root = Path.Combine(LauncherStorage.Root, "configuration-boolean-verification", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var results = new List<object>(); int failures = 0;
        Run(root, (name, test) =>
        {
            try { test(); results.Add(new { name, passed = true }); }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); }
        });
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { failures, results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    internal static void Run(string root, Action<string, Action> check)
    {
        check("Booléens : clic sur la case dessinée, bords, clavier, filtre et redimensionnement", () =>
        {
            var updates = new List<(string Key, string Value)>();
            using var fields = new ConfigurationFields([
                new() { Name = "PVP", Type = "boolean", Default = "true" },
                new() { Name = "php", Type = "boolean", Default = "false" },
                new() { Name = "Text", Type = "string", Default = "true" }
            ], [], (key, value, _) => updates.Add((key, value)));
            using var window = new EditorWindow(fields);
            Assert(updates.Count == 0, "Opening an editor wrote defaults");
            window.ClickCheckbox("PVP");
            Assert(updates.SequenceEqual(new[] { ("PVP", "false") }), "First click on the painted checkbox did not commit false");
            window.ClickCheckbox("PVP", -1);
            Assert(updates.Count == 2 && updates[^1] == ("PVP", "true"), "Left edge of painted checkbox is not clickable or toggled twice");
            window.ClickCheckbox("PVP", 1);
            Assert(updates.Count == 3 && updates[^1] == ("PVP", "false"), "Right edge of painted checkbox is not clickable or toggled twice");
            window.Space();
            Assert(updates.Count == 4 && updates[^1] == ("PVP", "true"), "Space did not toggle and commit once");
            var search = fields.Controls.OfType<TextBox>().Single(); search.Text = "php"; Application.DoEvents();
            window.ClickCheckbox("php");
            Assert(updates.Count == 5 && updates[^1] == ("php", "true"), "Filtered checkbox did not commit");
            search.Text = ""; window.ClientSize = new Size(660, 430); Application.DoEvents();
            window.ClickCheckbox("PVP"); fields.Commit();
            Assert(updates.Count == 6 && updates[^1] == ("PVP", "false"), "Resized checkbox did not commit");
            Assert(window.Cell("Text") is DataGridViewTextBoxCell, "A string literal was converted to a boolean");
        });

        check("Booléens INI : clic, sauvegarde serveur et relecture, y compris option inconnue", () =>
        {
            var preset = FixtureServer(Path.Combine(root, "boolean-ini"));
            File.WriteAllText(preset.Ini, "# preserved\nPVP=true\nphp=false\nText=true\n");
            var original = ServerFiles.Read(preset); var edited = new Dictionary<string, string>(original);
            ConfigOption[] schema = [
                new() { Name = "PVP", Type = "boolean", Default = "true" },
                new() { Name = "Public", Type = "boolean", Default = "false" },
                new() { Name = "Text", Type = "string", Default = "true" }
            ];
            using (var fields = new ConfigurationFields(schema, ServerFiles.IniValues(edited[".ini"]),
                (key, value, _) => edited[".ini"] = ServerFiles.UpdateValue(edited[".ini"], key, value)))
            using (var window = new EditorWindow(fields))
            {
                window.ClickCheckbox("PVP"); window.ClickCheckbox("php"); fields.Commit();
                ServerFiles.Save(preset, original, edited);
            }
            var saved = ServerFiles.Read(preset); var values = ServerFiles.IniValues(saved[".ini"]);
            Assert(values["PVP"] == "false" && values["php"] == "true", "Server boolean clicks did not persist to INI");
            Assert(!values.ContainsKey("Public") && values["Text"] == "true" && saved[".ini"].Contains("# preserved"), "Unedited fields or comments changed");
            Assert(File.Exists(preset.Ini + ".pzlauncher.bak"), "INI save did not keep a backup");
            int updates = 0;
            using var reloaded = new ConfigurationFields(schema, values, (_, _, _) => updates++);
            using var reloadWindow = new EditorWindow(reloaded);
            Assert(reloadWindow.Cell("PVP").Value is false && reloadWindow.Cell("php").Value is true && updates == 0, "Saved INI booleans did not reload correctly");
        });

        check("Booléens Lua : clic sur options sandbox et mod, sauvegarde et relecture typée", () =>
        {
            var preset = FixtureServer(Path.Combine(root, "boolean-lua"));
            File.WriteAllText(preset.Ini, "PVP=true\n");
            string path = Path.Combine(preset.ConfigDirectory, preset.Name + "_SandboxVars.lua");
            const string source = "-- preserved\nSandboxVars = { VERSION=6, PVP=true, php=false, ExampleMod={ Flag=true, Text=\"true\" }, }\n";
            File.WriteAllText(path, source);
            var original = ServerFiles.Read(preset); var edited = new Dictionary<string, string>(original);
            var document = new LuaConfigDocument(source);
            var schema = document.Values.Values.Select(v => new ConfigOption { Name = v.Name, Default = v.Value, Type = v.Type }).ToArray();
            using (var fields = new ConfigurationFields(schema, document.Values.ToDictionary(p => p.Key, p => p.Value.Value),
                (key, value, type) => edited["_SandboxVars.lua"] = new LuaConfigDocument(edited["_SandboxVars.lua"]).Set(key, value, type)))
            using (var window = new EditorWindow(fields))
            {
                window.ClickCheckbox("PVP"); window.ClickCheckbox("php"); window.ClickCheckbox("ExampleMod.Flag");
                fields.Commit(); ServerFiles.Save(preset, original, edited);
            }
            string saved = ServerFiles.Read(preset)["_SandboxVars.lua"]; var loaded = new LuaConfigDocument(saved);
            Assert(loaded.Values["PVP"].Value == "false" && loaded.Values["php"].Value == "true" && loaded.Values["ExampleMod.Flag"].Value == "false", "Sandbox boolean clicks did not persist");
            Assert(loaded.Values["PVP"].Type == "boolean" && loaded.Values["ExampleMod.Text"].Type == "string" && loaded.Values["ExampleMod.Text"].Value == "true", "Lua literal types changed");
            Assert(saved == source.Replace("PVP=true", "PVP=false").Replace("php=false", "php=true").Replace("Flag=true", "Flag=false"), "Unrelated Lua content changed");
            using var reloaded = new ConfigurationFields(schema, loaded.Values.ToDictionary(p => p.Key, p => p.Value.Value), (_, _, _) => { });
            using var reloadWindow = new EditorWindow(reloaded);
            Assert(reloadWindow.Cell("PVP").Value is false && reloadWindow.Cell("php").Value is true && reloadWindow.Cell("ExampleMod.Flag").Value is false, "Saved Lua booleans did not reload correctly");
        });

        check("Booléens sauvegarde : clic, enregistrement map_sand.bin et relecture sans perte", () =>
        {
            string world = Path.Combine(root, "boolean-world"); UsabilityVerification.CreateWorldFiles(world);
            string path = Path.Combine(world, "map_sand.bin");
            var fixture = new WorldSettingsDocument("map_sand.bin", File.ReadAllBytes(path));
            fixture.Values["PVP"] = "true"; fixture.Values["php"] = "false";
            byte[] before = fixture.Encode(); File.WriteAllBytes(path, before);
            var document = new WorldSettingsDocument("map_sand.bin", before);
            var original = new Dictionary<string, byte[]> { ["map_sand.bin"] = before };
            using (var fields = new ConfigurationFields([], document.Values, (key, value, _) => document.Values[key] = value))
            using (var window = new EditorWindow(fields))
            {
                window.ClickCheckbox("PVP"); window.ClickCheckbox("php"); fields.Commit();
                bool guarded = false;
                WorldSettingsDocument.Save(world, original, new() { ["map_sand.bin"] = document.Encode() }, () => guarded = true);
                Assert(guarded, "World save did not invoke its process guard");
            }
            byte[] after = File.ReadAllBytes(path); var loaded = new WorldSettingsDocument("map_sand.bin", after);
            Assert(loaded.Values["PVP"] == "false" && loaded.Values["php"] == "true", "World boolean clicks did not persist");
            Assert(loaded.Values["Zombies"] == "4" && loaded.Values["ExampleMod.Text"] == "éà中" && before.AsSpan(before.Length - 6).SequenceEqual(after.AsSpan(after.Length - 6)), "Unrelated sandbox data changed");
            using var reloaded = new ConfigurationFields([], loaded.Values, (_, _, _) => { });
            using var reloadWindow = new EditorWindow(reloaded);
            Assert(reloadWindow.Cell("PVP").Value is false && reloadWindow.Cell("php").Value is true, "Saved binary booleans did not reload correctly");
        });
    }

    private static ServerPreset FixtureServer(string root)
    {
        var preset = new ServerPreset { Name = "boolean_verification", CachePath = root };
        Directory.CreateDirectory(preset.ConfigDirectory); return preset;
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class EditorWindow : Form
    {
        private readonly DataGridView grid;
        protected override bool ShowWithoutActivation => true;

        internal EditorWindow(ConfigurationFields fields)
        {
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(900, 480);
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000);
            Controls.Add(fields); Show(); Application.DoEvents();
            grid = fields.Controls.OfType<DataGridView>().Single();
        }

        internal DataGridViewCell Cell(string key) => grid.Rows.Cast<DataGridViewRow>().Single(r => ((ConfigOption)r.Tag!).Name == key).Cells[1];

        internal void ClickCheckbox(string key, int edge = 0)
        {
            var cell = Cell(key); Assert(cell is DataGridViewCheckBoxCell, "No checkbox for " + key);
            grid.FirstDisplayedScrollingRowIndex = cell.RowIndex; Application.DoEvents();
            Rectangle bounds = grid.GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, false);
            
            
            int size = grid.LogicalToDeviceUnits(18);
            int x = bounds.X + (bounds.Width - size) / 2 + (edge < 0 ? 1 : edge > 0 ? size - 2 : size / 2);
            int y = bounds.Y + bounds.Height / 2;
            nint position = (nint)((y << 16) | (x & 0xffff));
            SendMessage(grid.Handle, 0x200, 0, position); 
            SendMessage(grid.Handle, 0x201, 1, position); 
            SendMessage(grid.Handle, 0x202, 0, position); 
            Application.DoEvents();
        }

        internal void Space()
        {
            SendMessage(grid.Handle, 0x100, 0x20, 1); 
            SendMessage(grid.Handle, 0x101, 0x20, unchecked((nint)0xc0000001)); 
            Application.DoEvents();
        }
    }
}
