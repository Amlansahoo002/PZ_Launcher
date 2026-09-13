namespace PZLauncher.Desktop;

internal sealed class WorldSettingsDialog : Form
{
    internal Task Ready { get; private set; } = Task.CompletedTask;
    private readonly Dictionary<string, byte[]> originals = [];
    private readonly Dictionary<string, WorldSettingsDocument> documents = [];
    private readonly List<ConfigurationFields> editors = [];
    private bool dirty;
    internal WorldSettingsDialog(string world, string cache, string installation)
    {
        Text = T("world.edit") + " · " + Path.GetFileName(world); Name = "WorldSettingsDialog";
        Font = Theme.Font(); BackColor = Theme.Background; ForeColor = Theme.Text;
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 640); MinimumSize = new Size(690, 500); Padding = new Padding(16);
        ShowInTaskbar = false; MinimizeBox = false; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var tabs = new ThemedTabs { Dock = DockStyle.Fill };
        var note = Theme.Label(T("world.editHelp"), 9, color: Theme.Muted); note.Dock = DockStyle.Top; note.Height = 70;
        var buttons = new ResponsiveToolbar { Dock = DockStyle.Bottom };
        var save = new ActionButton(T("settings.save")) { Width = 210, Height = 38, Primary = true };
        var close = new ActionButton(T("dialog.close")) { Width = 140, Height = 38 };
        buttons.Controls.AddRange([save, close]); Controls.Add(tabs); Controls.Add(note); Controls.Add(buttons);
        var sandboxPages = new List<(Panel Page, WorldSettingsDocument Document)>();
        void Fields(Panel page, WorldSettingsDocument document, IEnumerable<ConfigOption> schema)
        {
            foreach (Control c in page.Controls.Cast<Control>().ToArray()) c.Dispose();
            var editor = new ConfigurationFields(schema, document.Values, (key, value, _) => { document.Values[key] = value; dirty = true; });
            editors.Add(editor); page.Controls.Add(editor);
        }
        foreach (string name in new[] { "mods.txt", "map_ver.bin", "map_t.bin", "map_sand.bin" })
        {
            var page = new Panel { Text = name }; tabs.TabPages.Add(page);
            try
            {
                string file = WorldFiles.Within(world, name); if (!File.Exists(file)) throw new IOException(T("world.fileMissing", name));
                byte[] bytes = File.ReadAllBytes(file); var document = new WorldSettingsDocument(name, bytes);
                originals[name] = bytes; documents[name] = document;
                if (name == "map_sand.bin")
                { sandboxPages.Add((page, document)); page.Controls.Add(new Label { Dock = DockStyle.Fill, Text = T("config.loading"), ForeColor = Theme.Muted }); }
                else Fields(page, document, document.Options);
            }
            catch (Exception ex) { page.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Text = ex.Message, BackColor = Theme.Surface, ForeColor = Theme.Text }); }
        }
        async Task Load()
        {
            if (sandboxPages.Count == 0) return;
            try { var schema = await GameConfigurationSchema.Load(installation); if (!IsDisposed) foreach (var item in sandboxPages) Fields(item.Page, item.Document, schema.Sandbox); }
            catch (Exception ex)
            {
                if (IsDisposed) return; note.Text = T("world.editHelp") + "\n" + ex.Message;
                foreach (var item in sandboxPages) Fields(item.Page, item.Document, []);
            }
        }
        Ready = Load();
        bool Save()
        {
            try
            {
                foreach (var editor in editors.Where(e => !e.IsDisposed)) editor.Commit();
                var encoded = documents.ToDictionary(p => p.Key, p => p.Value.Encode());
                WorldSettingsDocument.Save(world, originals, encoded, () => GameProcessGuard.EnsureStopped(cache));
                dirty = false; note.Text = T("world.saved"); return true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        }
        save.Click += (_, _) => Save(); close.Click += (_, _) => Close();
        FormClosing += (_, e) =>
        {
            if (!dirty) return;
            var answer = MessageBox.Show(this, T("draft.question"), Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel || answer == DialogResult.Yes && !Save()) e.Cancel = true;
        };
    }
}
