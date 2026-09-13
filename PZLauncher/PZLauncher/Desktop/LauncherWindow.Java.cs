namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private bool preparingLaunch;
    private void BuildJavaMods(Control parent, ServerPreset? server = null)
    {
        var profile = server?.JavaProfile() ?? this.profile;
        var mods = server == null ? this.mods : ModCatalog.Scan(profile, InstallationLocator.ReadGameVersion(state.Installation));
        var selection = server == null ? this.selection : new ModSelection(profile.CachePath);
        if (server != null) { selection.Ids.Clear(); selection.Ids.AddRange(server.EnabledMods()); }
        string side = server == null ? "client" : "server";
        bool ResolveDrafts()
        {
            if (!this.ResolveDrafts()) return false;
            try { if (server != null) GameProcessGuard.EnsureStopped(server.CachePath); return true; }
            catch (Exception ex) { ShowError(ex); return false; }
        }
        var catalog = JavaMods.Scan(profile, mods, selection.Ids, state.Installation, side);
        var top = new Panel { Dock = DockStyle.Top, Height = 156 };
        var enabled = Check(T("java.enable"), profile.JavaLoaderEnabled); enabled.SetBounds(0, 0, 340, 34); top.Controls.Add(enabled);
        tips.SetToolTip(enabled, T("java.enableTip"));
        var loader = new ComboBox(); StyleCombo(loader);
        loader.Items.AddRange(JavaMods.Loaders.Select(JavaMods.LoaderName).ToArray());
        loader.SelectedIndex = Array.IndexOf(JavaMods.Loaders, profile.JavaLoader);
        loader.SetBounds(355, 3, 320, 30); top.Controls.Add(loader); tips.SetToolTip(loader, T("java.loader"));
        var runtime = new TextBox { ReadOnly = true, BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        runtime.SetBounds(0, 45, 480, 28); top.Controls.Add(runtime);
        var browse = new ActionButton(T("java.browse")) { Height = 32, Width = 150 };
        var auto = new ActionButton(T("java.auto")) { Height = 32, Width = 135 };
        top.Controls.AddRange([browse, auto]);
        var download = new ActionButton(T("loaderDownload.open")) { Name = "DownloadJavaLoader", Height = 35, Width = 245 };
        top.Controls.Add(download);
        var note = Theme.Label("", 9, color: Theme.Muted); top.Controls.Add(note);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
        var add = new ActionButton(T("java.add")) { Width = 145, Height = 42 };
        var folder = new ActionButton(T("java.folder")) { Width = 150, Height = 42 };
        var validate = new ActionButton(T("java.validate")) { Width = 190, Height = 42, Primary = true };
        var refresh = new ActionButton(T("refresh")) { Width = 110, Height = 42 };
        var up = new ActionButton("↑") { Width = 36, Height = 42 };
        var down = new ActionButton("↓") { Width = 36, Height = 42 };
        tips.SetToolTip(up, T("java.order")); tips.SetToolTip(down, T("java.order"));
        bottom.Controls.AddRange([add, folder, validate, refresh, up, down]);
        var grid = Theme.Grid(); grid.RowTemplate.Height = 32;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = T("column.enabled"), Width = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("column.mod"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 40, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("java.loader"), Width = 125, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("java.versionColumn"), Width = 85, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("column.availability"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 60, ReadOnly = true });
        parent.Controls.Add(grid); parent.Controls.Add(top); parent.Controls.Add(bottom);
        bool filling = false;
        void Arrange()
        {
            int width = top.ClientSize.Width;
            loader.Width = Math.Max(220, Math.Min(350, width - loader.Left));
            runtime.Width = Math.Max(200, width - 300);
            browse.Location = new Point(runtime.Right + 8, 42); auto.Location = new Point(browse.Right + 6, 42);
            note.SetBounds(0, runtime.Visible ? 83 : 45, runtime.Visible ? Math.Max(250, width - 260) : width, 65);
            download.Location = new(Math.Max(0, width - download.Width), 87);
        }
        top.Resize += (_, _) => Arrange();
        void Fill()
        {
            filling = true; grid.Rows.Clear();
            var requested = JavaMods.Requested(profile, catalog);
            foreach (var mod in catalog.OrderBy(m => m.Loader != profile.JavaLoader)
                .ThenBy(m => requested.Contains(m.Id) ? requested.IndexOf(m.Id) : int.MaxValue))
            {
                bool compatible = mod.Loader == profile.JavaLoader;
                bool selected = compatible && requested.Contains(mod.Id);
                string status = mod.Issue.Length > 0 ? mod.Issue
                    : mod.Owner.Length > 0 && mod.Owner != "@legacy" ? T(mod.OwnerEnabled ? "java.bundled" : "java.ownerDisabled", mod.Owner) : T("java.ready");
                if (!compatible) status = T("java.requiresLoader", JavaMods.LoaderName(mod.Loader)) + " · " + status;
                if (mod.DeclarationOnly) status += " · " + T("java.declared");
                status = T("java.side." + mod.Side) + " · " + status;
                int index = grid.Rows.Add(selected, mod.Name, JavaMods.LoaderName(mod.Loader), mod.Version, status);
                var row = grid.Rows[index]; row.Tag = mod;
                row.Cells[0].ReadOnly = !compatible || mod.Owner.Length > 0 && mod.Owner != "@legacy" || !mod.Available && !selected;
                foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = mod.Id + "\n" + (mod.Issue.Length > 0 ? mod.Issue : status) + "\n" + mod.Path;
                if (!mod.Available || !compatible || !mod.OwnerEnabled) row.DefaultCellStyle.ForeColor = Theme.Muted;
            }
            bool external = profile.JavaLoader is JavaMods.Leaf or JavaMods.ZombieBuddy;
            runtime.Visible = browse.Visible = auto.Visible = external;
            download.Visible = external;
            runtime.Text = external ? JavaMods.RuntimePath(profile, state.Installation) : "";
            tips.SetToolTip(runtime, runtime.Text);
            note.Text = T("java.note." + profile.JavaLoader);
            top.Height = external ? 156 : 118;
            up.Enabled = down.Enabled = profile.JavaLoader is JavaMods.None or JavaMods.Community;
            add.Enabled = profile.JavaLoader != JavaMods.ZombieBuddy;
            validate.Enabled = enabled.Checked;
            Arrange();
            filling = false;
        }
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || e.Graphics == null) return;
            e.PaintBackground(e.ClipBounds, true); int size = grid.LogicalToDeviceUnits(18);
            Theme.DrawCheck(e.Graphics, new Rectangle(e.CellBounds.X + (e.CellBounds.Width - size) / 2,
                e.CellBounds.Y + (e.CellBounds.Height - size) / 2, size, size), Convert.ToBoolean(e.Value), !grid.Rows[e.RowIndex].Cells[0].ReadOnly);
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus); e.Handled = true;
        };
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += (_, e) =>
        {
            if (filling || e.RowIndex < 0 || e.ColumnIndex != 0 || grid.Rows[e.RowIndex].Tag is not JavaModEntry mod) return;
            try
            {
                if (!ResolveDrafts()) return;
                var ids = JavaMods.Requested(profile, catalog); bool active = Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value);
                if (active) { if (!ids.Contains(mod.Id)) ids.Add(mod.Id); }
                else
                {
                    var dependant = JavaMods.Compatible(profile, catalog).FirstOrDefault(m => ids.Contains(m.Id) && m.Metadata.Requires.Contains(mod.Id));
                    if (dependant != null) throw new InvalidOperationException(T("error.dependent", dependant.Name));
                    ids.Remove(mod.Id);
                }
                var result = ModResolver.Resolve(JavaMods.Compatible(profile, catalog).Select(m => m.Metadata), ids);
                if (!result.Success && active) throw new InvalidOperationException(string.Join("\n", result.Issues));
                JavaMods.SetSelected(profile, (result.Success ? result.Ordered : ids).Where(id => !catalog.Any(m => m.Id == id && m.Owner.Length > 0 && m.Owner != "@legacy")));
                LauncherStorage.Save(state);
            }
            catch (Exception ex) { ShowError(ex); }
            finally { BeginInvoke(() => { if (!grid.IsDisposed) Fill(); }); }
        };
        enabled.CheckedChanged += (_, _) =>
        {
            if (filling) return;
            if (ResolveDrafts()) { profile.JavaLoaderEnabled = enabled.Checked; LauncherStorage.Save(state); Fill(); }
            else { filling = true; enabled.Checked = profile.JavaLoaderEnabled; filling = false; }
        };
        folder.Click += (_, _) => OpenFolder(JavaMods.Folder(profile));
        void Reload() { catalog = JavaMods.Scan(profile, mods, selection.Ids, state.Installation, side); Fill(); }
        loader.SelectedIndexChanged += (_, _) =>
        {
            if (filling || loader.SelectedIndex < 0) return;
            if (!ResolveDrafts()) { filling = true; loader.SelectedIndex = Array.IndexOf(JavaMods.Loaders, profile.JavaLoader); filling = false; return; }
            profile.JavaLoader = JavaMods.Loaders[loader.SelectedIndex]; LauncherStorage.Save(state); Reload();
        };
        browse.Click += (_, _) =>
        {
            if (!ResolveDrafts()) return;
            if (profile.JavaLoader == JavaMods.Leaf)
            {
                using var dialog = new FolderBrowserDialog { Description = T("java.leafFolder"), UseDescriptionForTitle = true };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                profile.LeafLibraryPath = dialog.SelectedPath;
            }
            else
            {
                using var dialog = new OpenFileDialog { Filter = "ZombieBuddy (*.jar)|*.jar", Title = T("java.browse") };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                profile.ZombieBuddyAgentPath = dialog.FileName;
            }
            LauncherStorage.Save(state); Fill();
        };
        auto.Click += (_, _) =>
        {
            if (!ResolveDrafts()) return;
            if (profile.JavaLoader == JavaMods.Leaf) profile.LeafLibraryPath = ""; else profile.ZombieBuddyAgentPath = "";
            LauncherStorage.Save(state); Fill();
        };
        download.Click += (_, _) =>
        {
            if (!ResolveDrafts()) return;
            var targetProfile = profile;
            using var dialog = new LoaderDownloadDialog(profile.JavaLoader, profile.CachePath, state.Installation, lifetime.Token, result =>
            {
                if (result.Loader == JavaMods.Leaf) targetProfile.LeafLibraryPath = result.RuntimePath;
                else { targetProfile.ZombieBuddyAgentPath = result.RuntimePath; targetProfile.Launch.ModFolders = WorkshopStore.PreferLocal(targetProfile.Launch.ModFolders); }
                if (!rendering) LauncherStorage.Save(state);
            });
            dialog.ShowDialog(this);
            if (!IsDisposed) { mods = ModCatalog.Scan(profile, InstallationLocator.ReadGameVersion(state.Installation)); Reload(); UpdateFooter(); }
        };
        void Move(int delta)
        {
            if (grid.CurrentRow?.Tag is not JavaModEntry mod || !ResolveDrafts()) return;
            try
            {
                var ids = JavaMods.Requested(profile, catalog); int at = ids.IndexOf(mod.Id);
                if (at < 0 || at + delta < 0 || at + delta >= ids.Count) return;
                (ids[at], ids[at + delta]) = (ids[at + delta], ids[at]);
                var result = ModResolver.Resolve(JavaMods.Compatible(profile, catalog).Select(m => m.Metadata), ids);
                if (!result.Success || !result.Ordered.SequenceEqual(ids)) throw new InvalidOperationException(T("java.orderBlocked"));
                JavaMods.SetOrder(profile, ids);
                LauncherStorage.Save(state); Fill();
                foreach (DataGridViewRow row in grid.Rows) if (row.Tag is JavaModEntry entry && entry.Id == mod.Id) grid.CurrentCell = row.Cells[1];
            }
            catch (Exception ex) { ShowError(ex); }
        }
        up.Click += (_, _) => Move(-1); down.Click += (_, _) => Move(1);
        refresh.Click += (_, _) => Reload();
        add.Click += (_, _) =>
        {
            if (!ResolveDrafts()) return;
            using var dialog = new OpenFileDialog { Filter = "Java mod (*.jar)|*.jar", Title = T("java.add"), Multiselect = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                string version = InstallationLocator.ReadGameVersion(state.Installation)?.ToString() ?? "";
                var candidates = dialog.FileNames.Select(path => JavaMods.Inspect(path, version, side: side)).ToList();
                var failed = candidates.Where(m => !m.Available).ToList();
                if (failed.Count > 0) throw new InvalidDataException(string.Join("\n", failed.Select(m => m.Name + ": " + m.Issue)));
                Directory.CreateDirectory(JavaMods.Folder(profile));
                foreach (var mod in candidates)
                {
                    string target = Path.Combine(JavaMods.Folder(profile), Path.GetFileName(mod.Path));
                    if (!Path.GetFullPath(target).Equals(Path.GetFullPath(mod.Path), StringComparison.OrdinalIgnoreCase)) File.Copy(mod.Path, target, false);
                }
                Reload();
            }
            catch (Exception ex) { ShowError(ex); Reload(); }
        };
        validate.Click += async (_, _) =>
        {
            if (!ResolveDrafts()) return;
            validate.Enabled = false; top.Enabled = bottom.Enabled = grid.Enabled = false;
            try
            {
                catalog = JavaMods.Scan(profile, mods, selection.Ids, state.Installation, side);
                var java = JavaMods.Prepare(profile, catalog, state.Installation, side);
                var plan = server == null ? launchService.CreateLaunchPlan(state.Installation, profile.AsGameProfile(), profile.AsJvmProfile()) : ServerFiles.Plan(state.Installation, server, includeJava: false);
                if (server == null) plan = RuntimeClients.Attach(plan, profile, state.Installation);
                RuntimeClients.CheckOverlayConflicts(plan.Arguments[plan.Arguments.ToList().IndexOf("-cp") + 1].Split(';').Where(Path.IsPathRooted), java);
                string report = await JavaMods.PreflightAsync(plan, java);
                if (!IsDisposed) ShowLocalizedMessage(T(java.Loader == JavaMods.Community ? "java.passed" : "java.checked") + "\n\n" + report, T("java.validate"), false);
            }
            catch (Exception ex) { if (!IsDisposed) ShowError(ex); }
            finally { if (!validate.IsDisposed) { top.Enabled = bottom.Enabled = grid.Enabled = true; Fill(); } }
        };
        Fill();
    }
}
