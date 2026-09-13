namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private void BuildRuntimeClients(Control parent, ServerPreset preset)
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 106 };
        var info = Theme.Label(T("runtime.localOnly"), 9, color: Theme.Muted); info.SetBounds(0, 0, 700, 59); top.Controls.Add(info);
        var addressLabel = Theme.Label(T("runtime.address"), 9); addressLabel.SetBounds(0, 69, 165, 30); top.Controls.Add(addressLabel);
        var address = new TextBox { Text = preset.ClientAddress, BackColor = Theme.Surface, ForeColor = Theme.Text }; address.SetBounds(170, 66, 250, 28); top.Controls.Add(address);
        var saveAddress = new ActionButton(T("settings.save")) { Width = 210, Height = 40 }; saveAddress.Location = new(430, 63); top.Controls.Add(saveAddress);
        top.SizeChanged += (_, _) => info.Width = top.ClientSize.Width;
        saveAddress.Click += (_, _) =>
        {
            try
            {
                var check = RuntimeClients.Clone(preset); check.ClientAddress = address.Text.Trim(); RuntimeClients.SetConnection(new(), check);
                preset.ClientAddress = check.ClientAddress; LauncherStorage.Save(state);
            }
            catch (Exception ex) { ShowError(ex); }
        };
        var grid = Theme.Grid(); grid.Name = "RuntimeClients"; grid.ReadOnly = true; grid.RowTemplate.Height = 34;
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("shell.profile"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("runtime.pack"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("runtime.cache"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 140 });
        void Fill()
        {
            grid.Rows.Clear();
            foreach (var p in state.Profiles.Where(p => p.DedicatedServerId == preset.Id))
            { int row = grid.Rows.Add(p.Name, p.RuntimePackId.Length == 0 ? "—" : p.RuntimePackId + " " + p.RuntimePackVersion, p.CachePath); grid.Rows[row].Tag = p; grid.Rows[row].Cells[2].ToolTipText = p.CachePath; }
        }
        var actions = new ResponsiveToolbar { Dock = DockStyle.Bottom, Name = "RuntimeClientActions", Padding = new Padding(0, 6, 0, 0) };
        parent.Controls.Add(grid); parent.Controls.Add(top); parent.Controls.Add(actions);
        ToolbarButton(actions, T("runtime.createClient"), 195, async () =>
        {
            if (!ResolveDrafts()) return;
            using var dialog = BuildProfileDialog(out var name); name.Text = preset.Name + " · client";
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            PlayerProfile? created = null; string clientName = name.Text.Trim();
            await WorldWork(parent, () =>
            {
                created = RuntimeClients.Create(preset, state.Installation, clientName, Path.Combine(LauncherStorage.Root, "profiles"), profile);
                var next = RuntimeClients.Clone(state); next.Profiles.Add(created); LauncherStorage.Save(next); state.Profiles = next.Profiles;
                return created.CachePath;
            });
            if (created != null) { profile = state.Profiles.Single(p => p.Id == profile.Id); ReloadProfileCombo(); Fill(); }
        });
        ToolbarButton(actions, T("runtime.associate"), 180, () =>
        {
            if (!ResolveDrafts()) return;
            using var dialog = new Form { Text = T("runtime.associate"), ClientSize = new(490, 150), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Background, ForeColor = Theme.Text, Font = Theme.Font(), MinimizeBox = false, MaximizeBox = false };
            var choices = new ComboBox { Left = 20, Top = 20, Width = 450 }; StyleCombo(choices); choices.Items.AddRange(state.Profiles.Where(p => p.OnlineServerId.Length == 0).ToArray()); if (choices.Items.Count > 0) choices.SelectedIndex = 0; dialog.Controls.Add(choices);
            var confirm = new ActionButton(T("runtime.associate")) { Left = 245, Top = 85, Width = 225, Height = 40, DialogResult = DialogResult.OK }; dialog.Controls.Add(confirm); dialog.AcceptButton = confirm;
            if (dialog.ShowDialog(this) != DialogResult.OK || choices.SelectedItem is not PlayerProfile selected) return;
            try
            {
                var next = RuntimeClients.Clone(state); var linked = next.Profiles.Single(p => p.Id == selected.Id);
                RuntimeClients.Associate(next, linked, preset, state.Installation); LauncherStorage.Save(next); state.Profiles = next.Profiles;
                profile = state.Profiles.Single(p => p.Id == profile.Id); ReloadProfileCombo(); Fill();
            }
            catch (Exception ex) { ShowError(ex); }
        });
        async Task Join()
        {
            if (grid.CurrentRow?.Tag is not PlayerProfile selected || !ResolveDrafts()) return;
            try
            {
                RuntimeClients.VerifyBinding(state, selected, state.Installation); RuntimeClients.SetConnection(selected, preset);
                if (game is { HasExited: false }) throw new IOException(T("status.gameRunning"));
                profile = selected; state.SelectedProfile = selected.Id; selection = new(selected.CachePath); LauncherStorage.Save(state);
                ReloadProfileCombo(); await RefreshLibraryAsync(); ShowPage("Jouer"); LaunchGame();
            }
            catch (Exception ex) { ShowError(ex); }
        }
        ToolbarButton(actions, T("online.join"), 170, async () => await Join(), true);
        grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await Join(); };
        ToolbarButton(actions, T("runtime.verify"), 190, async () =>
        {
            if (grid.CurrentRow?.Tag is not PlayerProfile selected) return;
            try
            {
                await Task.Run(() => RuntimeClients.VerifyBinding(state, selected, state.Installation));
                ShowLocalizedMessage(T(selected.RuntimePackSha256.Length > 0 ? "runtime.verified" : "runtime.bindingOnly") + "\r\n\r\n" + T("runtime.localOnly"), T("runtime.verify"), false);
            }
            catch (Exception ex) { ShowError(ex); }
        });
        var manage = new ActionButton(T("runtime.managePack")) { Width = 210, Height = 38 }; actions.Controls.Add(manage);
        manage.Enabled = preset.RuntimePackSha256.Length > 0;
        var menu = new ContextMenuStrip { BackColor = Theme.Surface, ForeColor = Theme.Text };
        manage.Click += (_, _) => menu.Show(manage, new Point(0, manage.Height)); parent.Disposed += (_, _) => menu.Dispose();
        void Sync(ServerPreset changed)
        {
            serverPreset = changed; profile = state.Profiles.First(p => p.Id == profile.Id); selection = new(profile.CachePath);
            ReloadProfileCombo(); ShowPage("Serveurs");
        }
        menu.Items.Add(T("runtime.replace"), null, async (_, _) =>
        {
            if (!ResolveDrafts()) return;
            using var file = new OpenFileDialog { Filter = "PZ runtime pack|pz-runtime-pack.json" }; if (file.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var pack = await Task.Run(() => ServerRuntimePack.Read(file.FileName, state.Installation));
                if (MessageBox.Show(this, T("runtime.replaceConfirm", pack.Id, pack.Version, state.Profiles.Count(p => p.DedicatedServerId == preset.Id)), T("runtime.replace"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                ServerPreset? changed = null;
                await WorldWork(parent, () => { changed = RuntimeRevisions.Replace(state, preset, file.FileName, state.Installation, Path.Combine(LauncherStorage.Root, "runtime-revisions")); return changed.CachePath; });
                if (changed != null) Sync(changed);
            }
            catch (Exception ex) { ShowError(ex); }
        });
        menu.Items.Add(T("runtime.rollback"), null, async (_, _) =>
        {
            if (!ResolveDrafts() || MessageBox.Show(this, T("runtime.rollbackConfirm"), T("runtime.rollback"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            ServerPreset? changed = null;
            await WorldWork(parent, () => { changed = RuntimeRevisions.Rollback(state, preset, state.Installation, Path.Combine(LauncherStorage.Root, "runtime-revisions")); return changed.CachePath; });
            if (changed != null) Sync(changed);
        });
        Fill();
    }
}
