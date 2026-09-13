using System.Data;
using PZ_ChunkWiper;

namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private string selectedWorld = "";
    private bool worldBusy;
    private Task worldScanTask = Task.CompletedTask;
    private MapWindowHost? mapWindowHost;
    private static TextBox Readout(string value = "") => new() { Text = value, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        Dock = DockStyle.Fill, BackColor = Theme.Background, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Font(9), WordWrap = false };
    private static ActionButton ToolbarButton(Control bar, string text, int width, Action action, bool primary = false)
    {
        var button = new ActionButton(text) { Width = width, Height = 36, Font = Theme.Font(9, true), Primary = primary, Margin = new Padding(0, 0, 6, 3) };
        button.Click += (_, _) => action(); bar.Controls.Add(button); return button;
    }
    private string WorldCache()
    {
        if (ftpWorld?.LocalRoot == selectedWorld) return Path.GetDirectoryName(selectedWorld)!;
        if (selectedWorld.Length == 0) return profile.CachePath;
        var directory = new DirectoryInfo(selectedWorld);
        return directory.Parent?.Parent?.Name == "Saves" ? directory.Parent.Parent.Parent!.FullName : profile.CachePath;
    }
    private async Task WorldWork(Control surface, Func<string> action)
    {
        if (worldBusy) return; worldBusy = true; pageHost.Enabled = false; surface.Enabled = false; UpdateFooter();
        try { string result = await Task.Run(action); if (!IsDisposed) ShowLocalizedMessage(result, T("world.result"), false); }
        catch (Exception ex) { if (!IsDisposed) ShowError(ex); }
        finally { worldBusy = false; if (!pageHost.IsDisposed) pageHost.Enabled = true; if (!surface.IsDisposed) surface.Enabled = true; if (!IsDisposed) UpdateFooter(); }
    }
    private void BuildWorldManager()
    {
        var panel = new Panel { Dock = DockStyle.Fill }; pageHost.Controls.Add(panel);
        var top = new ResponsiveToolbar { Dock = DockStyle.Top };
        var worlds = new ComboBox { Width = 360, DisplayMember = "Name" }; StyleCombo(worlds);
        foreach (var save in saves) worlds.Items.Add(save);
        if (ftpWorld != null) worlds.Items.Add(new SaveEntry("FTP · " + ftpWorld.RemoteDisplay, "FTP", ftpWorld.LocalRoot, DateTime.Now, 0));
        if (!Directory.Exists(selectedWorld)) selectedWorld = saves.FirstOrDefault()?.Path ?? "";
        worlds.SelectedItem = worlds.Items.Cast<SaveEntry>().FirstOrDefault(s => s.Path == selectedWorld); top.Controls.Add(worlds);
        var tabs = new ThemedTabs { Dock = DockStyle.Fill };
        var list = new Panel { Text = T("world.list") }; var wipe = new Panel { Text = "Chunk Wipe" }; var db = new Panel { Text = T("db.tab") };
        tabs.TabPages.Add(list); tabs.TabPages.Add(wipe); tabs.TabPages.Add(db);
        panel.Controls.Add(tabs);
        if (ftpWorld?.LocalRoot == selectedWorld) panel.Controls.Add(FtpWorldToolbar());
        panel.Controls.Add(top);
        ToolbarButton(top, T("world.browse"), 195, () =>
        {
            using var folder = new FolderBrowserDialog { Description = T("world.browse"), UseDescriptionForTitle = true };
            if (folder.ShowDialog(this) != DialogResult.OK) return;
            selectedWorld = folder.SelectedPath; ShowPage("Sauvegardes");
        });
        ToolbarButton(top, "FTP / FTPS", 140, OpenFtpWorld);
        if (ftpWorld?.LocalRoot != selectedWorld)
        {
            var pathHint = Theme.Label(Path.GetFileName(selectedWorld), 8, color: Theme.Muted); pathHint.Width = 225; pathHint.Height = 32; top.Controls.Add(pathHint); tips.SetToolTip(pathHint, selectedWorld);
        }
        else tips.SetToolTip(worlds, ftpWorld.RemoteDisplay);
        BuildSaveList(list);
        void RefreshTools()
        {
            mapWindowHost?.Dispose(); mapWindowHost = null;
            foreach (var page in new[] { wipe, db }) foreach (Control child in page.Controls.Cast<Control>().ToArray()) child.Dispose();
            BuildChunkWipe(wipe); BuildDatabaseBrowser(db);
        }
        worlds.SelectedIndexChanged += (_, _) => { if (!worldBusy && worlds.SelectedItem is SaveEntry save) { selectedWorld = save.Path; ShowPage("Sauvegardes"); } };
        if (savesGrid != null) savesGrid.SelectionChanged += (_, _) => { if (savesGrid.CurrentRow?.Tag is SaveEntry save && !worldBusy) worlds.SelectedItem = save; };
        RefreshTools();
    }
    private void BuildChunkWipe(Control parent)
    {
        if (!Directory.Exists(selectedWorld)) { parent.Controls.Add(Theme.Label(T("world.choose"))); return; }
        var embedded = parent;
        parent = new Panel { Dock = DockStyle.Fill }; embedded.Controls.Add(parent);
        var detachedNotice = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), Visible = false };
        embedded.Controls.Add(detachedNotice); parent.BringToFront();
        string root = selectedWorld, cache = WorldCache();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 150, WrapContents = true };
        var note = Theme.Label(T("wipe.help"), 9, color: Theme.Muted); note.Width = 770; note.Height = 40; top.Controls.Add(note); top.SetFlowBreak(note, true);
        NumericUpDown Number(string label, int value)
        {
            var name = Theme.Label(label, 8, true); name.Width = 27; name.Height = 27; name.TextAlign = ContentAlignment.MiddleLeft; top.Controls.Add(name);
            var box = new NumericUpDown { Minimum = -9999, Maximum = 9999, Value = value, Width = 72, BackColor = Theme.Surface, ForeColor = Theme.Text }; top.Controls.Add(box); return box;
        }
        var x1 = Number("X1", 0); var y1 = Number("Y1", 0); var x2 = Number("X2", 0); var y2 = Number("Y2", 0);
        var outside = Check(T("wipe.outside"), false); outside.Width = 220; outside.Height = 28; top.Controls.Add(outside); top.SetFlowBreak(outside, true);
        var zombies = Check(T("wipe.zombies"), false); zombies.Width = 170; zombies.Height = 28;
        var animals = Check(T("wipe.animals"), false); animals.Width = 170; animals.Height = 28;
        var vehicles = Check(T("wipe.vehicles"), false); vehicles.Width = 200; vehicles.Height = 28; top.Controls.AddRange([zombies, animals, vehicles]);
        var view = new MapViewerControl { Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text, ShowCellCoords = true, ShowGridChunk = false };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 79, WrapContents = true };
        var result = Theme.Label(T("wipe.noPreview"), 9, color: Theme.Muted); result.Width = 770; result.Height = 24; bottom.Controls.Add(result); bottom.SetFlowBreak(result, true);
        parent.Controls.Add(view); parent.Controls.Add(top); parent.Controls.Add(bottom);
        WipePlan? plan = null; ActionButton? apply = null;
        Rectangle Cells() => Rectangle.FromLTRB(Math.Min((int)x1.Value, (int)x2.Value), Math.Min((int)y1.Value, (int)y2.Value), Math.Max((int)x1.Value, (int)x2.Value) + 1, Math.Max((int)y1.Value, (int)y2.Value) + 1);
        void Changed() { plan = null; if (apply != null) apply.Enabled = false; result.Text = T("wipe.noPreview"); view.SelectedCellRect = Cells(); view.Invalidate(); }
        foreach (var number in new[] { x1, y1, x2, y2 }) number.ValueChanged += (_, _) => Changed();
        foreach (var check in new[] { outside, zombies, animals, vehicles }) check.CheckedChanged += (_, _) => Changed();
        view.CellSelectionChanged += (a, b, c, d) => { x1.Value = Math.Clamp(a, -9999, 9999); y1.Value = Math.Clamp(b, -9999, 9999); x2.Value = Math.Clamp(c, -9999, 9999); y2.Value = Math.Clamp(d, -9999, 9999); };
        async Task Scan()
        {
            try
            {
                var files = await Task.Run(() => ChunkWipe.Scan(root)); if (view.IsDisposed) return;
                view.MapChunks.Clear(); view.IsoRegionChunks.Clear(); view.BlamChunks.Clear(); view.ChunkDataCells.Clear(); view.ZPopCells.Clear(); view.APopCells.Clear(); view.MetaCells.Clear();
                foreach (var file in files)
                    switch (file.Layer) { case "map": view.MapChunks.Add((file.X, file.Y)); break; case "blam": view.BlamChunks.Add((file.X, file.Y)); break;
                        case "isoregiondata": view.IsoRegionChunks.Add((file.X, file.Y)); break; case "chunkdata": view.ChunkDataCells.Add((file.X, file.Y)); break;
                        case "zpop": view.ZPopCells.Add((file.X, file.Y)); break; case "apop": view.APopCells.Add((file.X, file.Y)); break; case "metacell": view.MetaCells.Add((file.X, file.Y)); break; }
                var chunks = files.Where(f => f.Layer == "map").ToList();
                if (chunks.Count > 0)
                {
                    int a = (int)Math.Floor(chunks.Min(f => f.X) / 32.0), b = (int)Math.Floor(chunks.Min(f => f.Y) / 32.0);
                    int c = (int)Math.Floor(chunks.Max(f => f.X) / 32.0), d = (int)Math.Floor(chunks.Max(f => f.Y) / 32.0);
                    view.FocusCells(Rectangle.FromLTRB(a, b, c + 1, d + 1));
                }
                view.Invalidate();
            }
            catch (Exception ex) { if (!parent.IsDisposed) ShowError(ex); }
        }
        ToolbarButton(bottom, T("wipe.preview"), 170, async () =>
        {
            try
            {
                Rectangle area = Cells(); bool invert = outside.Checked, z = zombies.Checked, a = animals.Checked, v = vehicles.Checked;
                parent.Enabled = false;
                plan = await Task.Run(() => { GameProcessGuard.EnsureStopped(cache); return ChunkWipe.Prepare(root, area, invert, z, a, v); });
                if (parent.IsDisposed) return;
                result.Text = T("wipe.summary", plan.Files.Count, plan.VehicleCount); apply!.Enabled = plan.Files.Count + plan.VehicleCount > 0;
                ShowLocalizedMessage(result.Text + "\r\n" + T(invert ? "wipe.outside" : "wipe.inside") + $"\r\nX: {area.Left}..{area.Right - 1} / Y: {area.Top}..{area.Bottom - 1}\r\n\r\n" +
                    string.Join("\r\n", plan.Files.GroupBy(f => f.Layer).Select(g => g.Key + ": " + g.Count())) + "\r\n\r\n" + string.Join("\r\n", plan.Files.Take(150).Select(f => Path.GetRelativePath(root, f.Path))), T("wipe.preview"), false);
            }
            catch (Exception ex) { if (!parent.IsDisposed) ShowError(ex); }
            finally { if (!parent.IsDisposed) parent.Enabled = true; }
        });
        apply = ToolbarButton(bottom, T("wipe.apply"), 195, async () =>
        {
            if (plan == null) return;
            if (ConfirmAction(T("wipe.confirm", root, plan.Files.Count, plan.VehicleCount), T("wipe.apply")) != DialogResult.OK) return;
            var chosen = plan; plan = null; apply!.Enabled = false;
            await WorldWork(parent, () => ChunkWipe.Execute(chosen, Path.Combine(LauncherStorage.Root, "backups", "worlds"), () => GameProcessGuard.EnsureStopped(cache)));
            if (!parent.IsDisposed) { Changed(); await Scan(); }
        }, true); apply.Enabled = false;
        ToolbarButton(bottom, T("world.backup"), 135, async () => await WorldWork(parent, () => { GameProcessGuard.EnsureStopped(cache); return WorldFiles.Backup(root, Path.Combine(LauncherStorage.Root, "backups", "worlds")); }));
        ToolbarButton(bottom, T("world.restore"), 195, async () =>
        {
            using var file = new OpenFileDialog { Filter = "ZIP (*.zip)|*.zip", InitialDirectory = Path.Combine(LauncherStorage.Root, "backups", "worlds") };
            if (file.ShowDialog(this) == DialogResult.OK) await WorldWork(parent, () => WorldFiles.RestoreAsCopy(file.FileName, Path.GetDirectoryName(root)!));
        });
        Action<string>? loadMapImage = null;
        ToolbarButton(top, T("wipe.background"), 165, () =>
        {
            using var file = new OpenFileDialog { Filter = "Map image|*.jpg;*.jpeg;*.png;*.bmp;*.gif" };
            if (file.ShowDialog(this) != DialogResult.OK) return;
            try { loadMapImage!(file.FileName); } catch (Exception ex) { ShowError(ex); }
        });
        top.SetFlowBreak(top.Controls[^1], true);
        var host = new MapWindowHost(this, embedded, parent, T("wipe.windowTitle", Path.GetFileName(root)), () => worldBusy || !parent.Enabled);
        mapWindowHost = host;
        var detach = ToolbarButton(top, T("wipe.detach"), 275, () => host.Toggle());
        loadMapImage = BuildMapCalibration(top, view, Changed);
        ToolbarButton(detachedNotice, T("wipe.reattach"), 280, () => host.Toggle());
        host.Changed += () =>
        {
            if (!detach.IsDisposed) detach.Text = T(host.Window == null ? "wipe.detach" : "wipe.reattach");
            if (!detachedNotice.IsDisposed) detachedNotice.Visible = host.Window != null;
        };
        worldScanTask = Scan();
    }
    private DialogResult ConfirmAction(string message, string title)
    {
        using var dialog = CreateMessageDialog(message, title, false);
        var accept = Descendants(dialog).OfType<ActionButton>().Single();
        var cancel = new ActionButton(T("dialog.cancel")) { Width = 170, Height = 40, DialogResult = DialogResult.Cancel };
        accept.Parent!.Controls.Add(cancel); dialog.CancelButton = cancel; dialog.AcceptButton = cancel;
        return dialog.ShowDialog(this);
    }
    private void BuildDatabaseBrowser(Control parent)
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 84, WrapContents = true };
        var paths = new ComboBox { Width = 275 }; StyleCombo(paths);
        if (Directory.Exists(selectedWorld)) foreach (string file in Directory.EnumerateFiles(selectedWorld, "*.db")) paths.Items.Add(file);
        string auth = Path.Combine(WorldCache(), "db");
        if (Directory.Exists(auth)) foreach (string file in Directory.EnumerateFiles(auth, "*.db")) paths.Items.Add(file);
        paths.Format += (_, e) => { if (e.ListItem is string path) e.Value = Path.GetFileName(path); }; paths.FormattingEnabled = true;
        var tables = new ComboBox { Width = 205 }; StyleCombo(tables); top.Controls.AddRange([paths, tables]);
        int offset = 0; var count = Theme.Label(T("db.paging"), 8, color: Theme.Muted); count.Width = 330; count.Height = 24;
        var detail = Readout(T("db.help")); detail.Height = 175; detail.Dock = DockStyle.Bottom;
        parent.SizeChanged += (_, _) => detail.Height = Math.Clamp((int)(parent.ClientSize.Height * .32), 90, 200);
        var grid = Theme.Grid(); grid.ReadOnly = true; grid.RowTemplate.Height = 29; grid.AutoGenerateColumns = false;
        parent.Controls.Add(grid); parent.Controls.Add(detail); parent.Controls.Add(top);
        DataTable? current = null; int detailRevision = 0;
        void LoadRows()
        {
            if (paths.SelectedItem is not string path || tables.SelectedItem is not string table) return;
            try
            {
                detailRevision++; current?.Dispose(); current = DatabaseReader.Read(path, table, offset); grid.Columns.Clear(); grid.Rows.Clear();
                foreach (DataColumn column in current.Columns) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = column.ColumnName, HeaderText = column.ColumnName, Width = 140 });
                foreach (DataRow row in current.Rows)
                {
                    int index = grid.Rows.Add(row.ItemArray.Select(v => v is byte[] bytes ? $"BLOB ({bytes.Length:N0} B)" : v ?? DBNull.Value).ToArray()); grid.Rows[index].Tag = row;
                }
                count.Text = T("db.rows", current.Rows.Count == 0 ? 0 : offset + 1, offset + current.Rows.Count); detail.Text = T("db.help");
            }
            catch (Exception ex) { ShowError(ex); }
        }
        paths.SelectedIndexChanged += (_, _) =>
        {
            if (paths.SelectedItem is not string path) return;
            try { tables.Items.Clear(); tables.Items.AddRange(DatabaseReader.Tables(path).ToArray()); if (tables.Items.Count > 0) tables.SelectedIndex = 0; }
            catch (Exception ex) { ShowError(ex); }
        };
        tables.SelectedIndexChanged += (_, _) => { offset = 0; LoadRows(); };
        ToolbarButton(top, T("db.open"), 145, () => { using var file = new OpenFileDialog { Filter = "SQLite (*.db)|*.db" }; if (file.ShowDialog(this) == DialogResult.OK) { paths.Items.Add(file.FileName); paths.SelectedItem = file.FileName; } });
        ToolbarButton(top, "‹", 35, () => { offset = Math.Max(0, offset - 200); LoadRows(); });
        ToolbarButton(top, "›", 35, () => { if (current?.Rows.Count == 200) { offset += 200; LoadRows(); } });
        top.Controls.Add(count);
        ToolbarButton(top, T("db.exportBlob"), 180, () =>
        {
            if (grid.CurrentCell == null || grid.CurrentRow?.Tag is not DataRow row || row[grid.CurrentCell.ColumnIndex] is not byte[] bytes) return;
            using var file = new SaveFileDialog { Filter = "BLOB (*.bin)|*.bin", FileName = "blob.bin" };
            if (file.ShowDialog(this) == DialogResult.OK) try { File.WriteAllBytes(file.FileName, bytes); } catch (Exception ex) { ShowError(ex); }
        });
        grid.CellClick += async (_, e) =>
        {
            int revision = ++detailRevision;
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Rows[e.RowIndex].Tag is not DataRow row || paths.SelectedItem is not string path || tables.SelectedItem is not string table) return;
            if (row[e.ColumnIndex] is not byte[] bytes) { detail.Text = Convert.ToString(row[e.ColumnIndex]) ?? "NULL"; return; }
            var values = row.Table.Columns.Cast<DataColumn>().Where(c => row[c] is not byte[]).ToDictionary(c => c.ColumnName, c => row[c]);
            string column = row.Table.Columns[e.ColumnIndex].ColumnName;
            detail.Text = T("db.decoding"); string report = await Task.Run(() => DatabaseReader.Blob(path, table, column, bytes, values));
            if (!detail.IsDisposed && revision == detailRevision) detail.Text = report;
        };
        parent.Disposed += (_, _) => current?.Dispose();
        if (paths.Items.Count > 0) paths.SelectedIndex = 0;
    }
}
