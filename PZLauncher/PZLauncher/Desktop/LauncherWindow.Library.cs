using System.Diagnostics;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private void BuildMods()
    {
        var tabs = new ThemedTabs { Dock = DockStyle.Fill };
        var traditional = new Panel { Text = T("mods.traditional") };
        var java = new Panel { Text = T("java.tab") };
        var workshop = new Panel { Text = T("workshop.tab") };
        tabs.TabPages.Add(traditional); tabs.TabPages.Add(java); tabs.TabPages.Add(workshop); pageHost.Controls.Add(tabs);
        BuildTraditionalMods(traditional); BuildJavaMods(java); BuildWorkshop(workshop, tabs);
        
        java.VisibleChanged += (_, _) => { if (java.Visible) { foreach (Control child in java.Controls.Cast<Control>().ToArray()) child.Dispose(); BuildJavaMods(java); } };
    }
    private void BuildTraditionalMods(Control parent)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        parent.Controls.Add(panel);
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 94 };
        var note = Theme.Label(T("mods.note"), 9, color: Theme.Muted);
        note.SetBounds(0, 0, 930, 26); note.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right; toolbar.Controls.Add(note);
        modSearch = new TextBox { PlaceholderText = T("mods.search"), BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        modSearch.SetBounds(0, 43, 400, 30); toolbar.Controls.Add(modSearch);
        modFilter = new ComboBox(); StyleCombo(modFilter); modFilter.Items.AddRange([T("mods.all"), T("mods.selected"), "Workshop", T("mods.local"), T("mods.unavailable")]);
        modFilter.SelectedIndex = 0; modFilter.SetBounds(416, 43, 194, 32); toolbar.Controls.Add(modFilter);
        var refresh = new ActionButton(T("refresh")) { Width = 130 }; refresh.SetBounds(626, 38, 130, 38); toolbar.Controls.Add(refresh);
        refresh.Click += async (_, _) =>
        {
            if (!ResolveDrafts()) return;
            selection = new(profile.CachePath); await RefreshLibraryAsync();
        };
        var bottom = new ResponsiveToolbar { Name = "ModActions", Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(0, 8, 0, 6) };
        ActionButton Tool(string text, int width, Action action, bool primary = false)
        {
            var button = new ActionButton(text) { Width = width, Height = 38, Primary = primary, Margin = new Padding(0, 0, 4, 0) };
            button.Width = Math.Max(width, Math.Min(280, TextRenderer.MeasureText(text, button.Font,
                Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + 28));
            button.Click += (_, _) => action(); bottom.Controls.Add(button); return button;
        }
        Tool(T("mods.up"), 85, () => MoveMod(-1));
        Tool(T("mods.down"), 100, () => MoveMod(1));
        Tool(T("mods.folder"), 140, () => { if (modGrid?.CurrentRow?.Tag is InstalledMod mod) OpenFolder(modFilter?.SelectedIndex == 3 ? mod.Copies.FirstOrDefault(c => c.Source is "Local" or "SteamCMD")?.Location ?? mod.Location : mod.Location); });
        Tool(T("mods.auto"), 130, () => { if (ArrangeMods()) FillMods(); });
        Tool(T("mods.checkAll"), 105, () =>
        {
            var result = ModResolver.Resolve(mods, selection.Ids);
            ShowLocalizedMessage(result.Success ? T("mods.valid") : string.Join("\r\n", result.Issues), T("mods.checkAll"), false);
        });
        Tool(T("mods.save"), 220, () => { if (SaveModChanges()) footerDetail.Text = T("mods.saved"); }, true);
        Tool(T("mods.scanDetails"), 160, () => ShowLocalizedMessage(T("mods.scanSummary", profile.CachePath, profile.Launch.ModFolders) + "\r\n\r\n" +
            (modScanIssues.Count == 0 ? T("mods.scanClean") : string.Join("\r\n\r\n", modScanIssues)), T("mods.scanDetails"), false));
        toolbar.SizeChanged += (_, _) =>
        {
            note.Width = toolbar.ClientSize.Width;
            refresh.Left = toolbar.Width - refresh.Width;
            modFilter.Left = refresh.Left - modFilter.Width - 8;
            modSearch.Width = Math.Max(150, modFilter.Left - 8);
        };
        modGrid = Theme.Grid();
        modGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = T("column.enabled"), Width = 70 });
        modGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Order", HeaderText = T("column.order"), Width = 72, ReadOnly = true });
        modGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = T("column.mod"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 62, ReadOnly = true });
        modGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = T("column.source"), Width = 110, ReadOnly = true });
        modGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Target", HeaderText = T("column.availability"), Width = 150, ReadOnly = true });
        modGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = T("column.id"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 38, ReadOnly = true });
        modGrid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || e.Graphics == null) return;
            e.PaintBackground(e.ClipBounds, true);
            int size = modGrid.LogicalToDeviceUnits(18);
            Theme.DrawCheck(e.Graphics, new Rectangle(e.CellBounds.X + (e.CellBounds.Width - size) / 2,
                e.CellBounds.Y + (e.CellBounds.Height - size) / 2, size, size), Convert.ToBoolean(e.Value), modGrid.Enabled);
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);
            e.Handled = true;
        };
        modGrid.CurrentCellDirtyStateChanged += (_, _) => { if (modGrid.IsCurrentCellDirty) modGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        modGrid.CellValueChanged += (_, e) =>
        {
            if (fillingMods || e.RowIndex < 0 || e.ColumnIndex != 0 || modGrid.Rows[e.RowIndex].Tag is not InstalledMod mod) return;
            SetModEnabled(mod, Convert.ToBoolean(modGrid.Rows[e.RowIndex].Cells[0].Value));
        };
        modGrid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || modGrid.Rows[e.RowIndex].Tag is not InstalledMod mod) return;
            e.ToolTipText = mod.Name + "\n" + mod.Id + "\n" + mod.Location +
                (mod.Requires.Length > 0 ? T("mods.dependencies") + string.Join(", ", mod.Requires) : "") +
                (modGrid.Rows[e.RowIndex].ErrorText.Length > 0 ? "\n" + modGrid.Rows[e.RowIndex].ErrorText : "");
        };
        modGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex == 0 || modGrid.Rows[e.RowIndex].Tag is not InstalledMod mod) return;
            ShowLocalizedMessage(T("mods.copyPriority", mod.Id, mod.Location, profile.Launch.ModFolders) + "\r\n\r\n" +
                string.Join("\r\n\r\n", mod.Copies.Select(c => ModLabel(c.Source) + " · " + c.Target + "\r\n" + c.Location + (c.Issue.Length > 0 ? "\r\n" + c.Issue : ""))), mod.Name, false);
        };
        modSearch.TextChanged += (_, _) => FillMods(); modFilter.SelectedIndexChanged += (_, _) => FillMods();
        panel.Controls.Add(modGrid); panel.Controls.Add(toolbar); panel.Controls.Add(bottom);
        FillMods();
    }
    private void FillMods()
    {
        if (modGrid == null) return;
        string query = modSearch?.Text.Trim() ?? ""; int filter = modFilter?.SelectedIndex ?? 0;
        IEnumerable<InstalledMod> visible = mods.Where(m => m.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || m.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
        visible = filter switch
        {
            1 => visible.Where(m => selection.Ids.Contains(m.Id)).OrderBy(m => selection.Ids.IndexOf(m.Id)),
            2 => visible.Where(m => m.Source == "Workshop"),
            3 => visible.Where(m => m.Source is "Local" or "SteamCMD" || m.Copies.Any(c => c.Source is "Local" or "SteamCMD")),
            4 => visible.Where(m => !m.Available), _ => visible
        };
        fillingMods = true; modGrid.Rows.Clear();
        foreach (var mod in visible)
        {
            int order = selection.Ids.IndexOf(mod.Id);
            string availability = mod.Source == "Introuvable" ? T("mods.missing") :
                (mod.Source is "Workshop" or "Staged") && !profile.Steam ? T("mods.needsSteam") : ModLabel(mod.Target);
            var dependencyReport = mod.Available ? ModResolver.Resolve(mods, [mod.Id]) : null;
            if (dependencyReport is { Success: false }) availability = T("mods.issues", dependencyReport.Issues.Count);
            if (mod.Issue.Length > 0) availability = mod.Issue;
            if (mod.Copies.Count > 1) availability = T("mods.copies", mod.Copies.Count) + " · " + availability;
            string sources = mod.Copies.Count > 1 ? string.Join(" + ", mod.Copies.Select(c => ModLabel(c.Source)).Distinct()) : ModLabel(mod.Source);
            int index = modGrid.Rows.Add(order >= 0, order < 0 ? "—" : (order + 1).ToString(), mod.Name, sources, availability, mod.Id);
            var row = modGrid.Rows[index]; row.Tag = mod;
            row.ErrorText = dependencyReport is { Success: false } ? string.Join("\r\n", dependencyReport.Issues) : mod.Issue;
            row.Cells["Target"].ToolTipText = row.ErrorText;
            if (!mod.Available) row.DefaultCellStyle.ForeColor = Theme.Muted;
        }
        fillingMods = false;
    }
    private void SetModEnabled(InstalledMod mod, bool enabled)
    {
        try
        {
            var requested = selection.Ids.ToList();
            if (enabled) { if (!requested.Contains(mod.Id)) requested.Add(mod.Id); }
            else
            {
                var dependant = mods.FirstOrDefault(m => requested.Contains(m.Id) && m.Requires.Contains(mod.Id));
                if (dependant != null) throw new InvalidOperationException(T("error.dependent", dependant.Name));
                requested.Remove(mod.Id);
            }
            var result = ModResolver.Resolve(mods, requested);
            if (!result.Success && enabled) throw new InvalidOperationException(string.Join("\r\n", result.Issues));
            selection.Ids.Clear(); selection.Ids.AddRange(result.Success ? result.Ordered : requested);
            modsDirty = true; UpdateFooter();
            footerDetail.Text = result.Added.Count > 0 ? T("mods.autoAdded", string.Join(", ", result.Added)) : footerDetail.Text + T("status.unsaved");
        }
        catch (Exception ex) { ShowError(ex); }
        fillingMods = true;
        if (modGrid != null) foreach (DataGridViewRow row in modGrid.Rows)
        {
            if (row.Tag is not InstalledMod entry) continue;
            int index = selection.Ids.IndexOf(entry.Id); row.Cells[0].Value = index >= 0; row.Cells[1].Value = index < 0 ? "—" : (index + 1).ToString();
        }
        fillingMods = false;
    }
    private void MoveMod(int direction)
    {
        if (modGrid?.CurrentRow?.Tag is not InstalledMod mod) return;
        int from = selection.Ids.IndexOf(mod.Id), to = from + direction;
        if (from < 0 || to < 0 || to >= selection.Ids.Count) return;
        (selection.Ids[from], selection.Ids[to]) = (selection.Ids[to], selection.Ids[from]);
        var resolved = ModResolver.Resolve(mods, selection.Ids);
        if (!resolved.Success || !resolved.Ordered.SequenceEqual(selection.Ids))
        {
            (selection.Ids[from], selection.Ids[to]) = (selection.Ids[to], selection.Ids[from]);
            ShowError(new InvalidOperationException(T("mods.orderConstraint"))); return;
        }
        modsDirty = true; FillMods();
        if (modGrid != null) foreach (DataGridViewRow row in modGrid.Rows)
            if (row.Tag is InstalledMod entry && entry.Id == mod.Id) { modGrid.CurrentCell = row.Cells[2]; break; }
    }
    private bool ArrangeMods()
    {
        var result = ModResolver.Resolve(mods, selection.Ids);
        if (!result.Success) { ShowError(new InvalidOperationException(string.Join("\r\n", result.Issues))); return false; }
        if (!result.Ordered.SequenceEqual(selection.Ids))
        {
            selection.Ids.Clear(); selection.Ids.AddRange(result.Ordered); modsDirty = true; UpdateFooter();
        }
        return true;
    }
    private bool SaveModChanges()
    {
        if (!modsDirty) return true;
        try { if (!ArrangeMods()) return false; GameProcessGuard.EnsureStopped(profile.CachePath); selection.Save(); modsDirty = false; UpdateFooter(); return true; }
        catch (Exception ex) { ShowError(ex); return false; }
    }

    private void BuildSaves()
    {
        BuildWorldManager();
    }
    private void BuildSaveList(Control parent)
    {
        var panel = new Panel { Dock = DockStyle.Fill }; parent.Controls.Add(panel);
        var top = new Panel { Dock = DockStyle.Top, Height = 68 };
        var note = Theme.Label(T("saves.note", profile.Name), 10, color: Theme.Muted);
        note.SetBounds(0, 4, 950, 44); note.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; top.Controls.Add(note);
        var bottom = new ResponsiveToolbar { Dock = DockStyle.Bottom, Padding = new Padding(0, 8, 0, 6), Name = "WorldActions" };
        void Edit(SaveEntry save)
        {
            try { using var editor = new WorldSettingsDialog(save.Path, profile.CachePath, state.Installation); editor.ShowDialog(this); }
            catch (Exception ex) { ShowError(ex); }
        }
        ToolbarButton(bottom, T("world.edit"), 210, () => { if (savesGrid?.CurrentRow?.Tag is SaveEntry save) Edit(save); }, true);
        ToolbarButton(bottom, T("saves.open"), 200, () => { if (savesGrid?.CurrentRow?.Tag is SaveEntry save) OpenFolder(save.Path); });
        ToolbarButton(bottom, T("world.delete"), 175, async () =>
        {
            if (savesGrid?.CurrentRow?.Tag is not SaveEntry save) return;
            if (MessageBox.Show(this, T("world.deleteConfirm", save.Name, save.Path), T("world.delete"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try { await Task.Run(() => WorldSettingsDocument.Delete(profile.CachePath, save.Path)); await RefreshLibraryAsync(); }
            catch (Exception ex) { ShowError(ex); }
        });
        ToolbarButton(bottom, T("refresh"), 135, async () => await RefreshLibraryAsync());
        ToolbarButton(bottom, T("saves.folder"), 230, () => OpenFolder(Path.Combine(profile.CachePath, "Saves")));
        savesGrid = Theme.Grid(); savesGrid.ReadOnly = true;
        savesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("column.world"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 65 });
        savesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("column.mode"), Width = 140 });
        savesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("column.modified"), Width = 225 });
        savesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("column.files"), Width = 100 });
        savesGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && savesGrid.Rows[e.RowIndex].Tag is SaveEntry save) Edit(save); };
        panel.Controls.Add(savesGrid); panel.Controls.Add(top); panel.Controls.Add(bottom); FillSaves();
    }
    private void FillSaves()
    {
        if (savesGrid == null) return;
        savesGrid.Rows.Clear();
        foreach (var save in saves)
        {
            int row = savesGrid.Rows.Add(save.Name, save.Mode, save.Modified.ToString("dd MMM yyyy  ·  HH:mm", L.Culture), save.Files.ToString("N0", L.Culture));
            savesGrid.Rows[row].Tag = save; savesGrid.Rows[row].Cells[0].ToolTipText = save.Path;
        }
        if (saves.Count == 0)
        {
            int row = savesGrid.Rows.Add(T("saves.empty"), "", "", "");
            savesGrid.Rows[row].DefaultCellStyle.ForeColor = Theme.Muted;
        }
    }

    private void BuildNews()
    {
        var panel = new Panel { Dock = DockStyle.Fill }; pageHost.Controls.Add(panel);
        var top = new Panel { Dock = DockStyle.Top, Height = 57 };
        string status = newsService.FromCache ? T("news.cached", newsService.LastUpdated.ToLocalTime().ToString("g", L.Culture)) : T("news.source");
        if (news.Count == 0 && newsService.Error.Length > 0) status = T("news.unreachable");
        var label = Theme.Label(status, 9, color: Theme.Muted); label.Dock = DockStyle.Fill; label.Padding = new Padding(0, 8, 8, 0); top.Controls.Add(label);
        var refresh = new ActionButton(T("refresh")) { Dock = DockStyle.Right, Width = 135 };
        refresh.Click += async (_, _) => { refresh.Enabled = false; await RefreshNewsAsync(); }; top.Controls.Add(refresh);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        panel.Controls.Add(scroll); panel.Controls.Add(top);
        int y = 0;
        foreach (var item in news)
        {
            var card = new SurfacePanel { Location = new Point(0, y), Size = new Size(Math.Max(500, pageHost.ClientSize.Width - 80), 177) };
            var source = Theme.Label(item.Published.ToLocalTime().ToString("dd MMMM yyyy", L.Culture) + "   /   THE INDIE STONE", 8, true, Theme.Muted);
            source.SetBounds(22, 18, card.Width - 44, 23); card.Controls.Add(source);
            var title = Theme.Label(item.Title, 16, true); title.SetBounds(20, 46, card.Width - 44, 35); title.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; card.Controls.Add(title);
            var summary = Theme.Label(item.Summary, 9.5f, color: Theme.Muted); summary.SetBounds(22, 89, card.Width - 245, 61); summary.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; card.Controls.Add(summary);
            var open = new ActionButton(T("news.read")) { Width = 203 }; open.SetBounds(card.Width - 225, 115, 203, 39); open.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            open.Click += (_, _) => OpenNews(item); card.Controls.Add(open); scroll.Controls.Add(card); y += 191;
            void ArrangeArticle()
            {
                card.Width = Math.Max(500, scroll.ClientSize.Width - 20);
                source.Anchor = title.Anchor = summary.Anchor = open.Anchor = AnchorStyles.Left | AnchorStyles.Top;
                source.Width = title.Width = card.Width - 44;
                summary.Width = Math.Max(240, card.Width - 267);
                open.Left = card.Width - 225;
            }
            scroll.SizeChanged += (_, _) => ArrangeArticle();
            ArrangeArticle();
        }
        if (news.Count == 0)
        {
            var empty = Theme.Label(T("news.empty"), 14, color: Theme.Muted);
            empty.SetBounds(20, 55, 700, 90); scroll.Controls.Add(empty);
            var website = new ActionButton(T("news.open")) { Width = 245 }; website.SetBounds(20, 160, 245, 44); website.Click += (_, _) => OpenUrl("https://projectzomboid.com/blog/news/"); scroll.Controls.Add(website);
        }
    }
    private void BuildAbout()
    {
        var flow = new FlowLayoutPanel { Name = "CommunityCredits", Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 14, 20, 14) };
        pageHost.Controls.Add(flow);
        var title = Theme.Label("PZLauncher Community", 25, true); title.Height = 49; flow.Controls.Add(title);
        var intro = Theme.Label(T("about.tagline"), 15, color: Theme.AccentText); intro.Height = 37; flow.Controls.Add(intro);
        var copy = Theme.Label(T("about.copy"), 10, color: Theme.Muted); flow.Controls.Add(copy);
        var creditTitle = Theme.Label(T("about.credits"), 12, true); creditTitle.Height = 33; flow.Controls.Add(creditTitle);
        void Credit(string name, string text, string buttonText, string url)
        {
            var row = new SurfacePanel { Height = 88, Margin = new Padding(0, 0, 0, 8) };
            var heading = Theme.Label(name, 11, true); var note = Theme.Label(text, 9, color: Theme.Muted);
            var link = new ActionButton(buttonText) { Width = 195, Height = 38, Enabled = url.Length > 0 };
            if (url.Length > 0) link.Click += (_, _) => OpenUrl(url);
            row.Controls.AddRange([heading, note, link]); flow.Controls.Add(row);
            row.Resize += (_, _) =>
            {
                heading.SetBounds(14, 9, Math.Max(160, row.Width - 235), 25);
                note.SetBounds(14, 36, Math.Max(160, row.Width - 235), 44);
                link.Location = new(row.Width - link.Width - 14, 25);
            };
        }
        Credit("Leaf", T("about.leafCredit"), "GitHub · Leaf ↗", CommunityLinks.Leaf);
        Credit("ZombieBuddy", T("about.buddyCredit"), "GitHub · ZombieBuddy ↗", CommunityLinks.ZombieBuddy);
        Credit("Project Zomboid", T("about.pzCredit"), T("about.discord"), CommunityLinks.Discord);
        Credit("PZLauncher Community", T("about.futureRepository"),
            T(CommunityLinks.LauncherRepository.Length == 0 ? "about.githubSoon" : "about.github"), CommunityLinks.LauncherRepository);
        var buttons = new FlowLayoutPanel { Height = 48, WrapContents = false, Margin = Padding.Empty };
        ToolbarButton(buttons, T("about.site"), 280, () => OpenUrl("https://projectzomboid.com/"));
        ToolbarButton(buttons, T("about.folder"), 230, () => OpenFolder(LauncherStorage.Root)); flow.Controls.Add(buttons);
        void Arrange()
        {
            int width = Math.Max(250, flow.ClientSize.Width - flow.Padding.Horizontal - 24);
            foreach (Control control in flow.Controls) control.Width = width;
            copy.Height = TextRenderer.MeasureText(copy.Text, copy.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height + 12;
        }
        flow.Resize += (_, _) => Arrange(); Arrange();
    }
}
