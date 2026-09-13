namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private string onlineSelectedId = "";
    private bool onlineBusy, closeAfterOnline;
    private CancellationTokenSource? onlineCancellation;
    private List<OnlineServerInfo> onlinePublic = [];
    private bool onlinePublicPartial;
    private string onlineResult = "";
    private OnlineFavorite? onlineDraft;
    private bool onlineDirty;
    private int onlineTabIndex;
    private readonly OnlineServerFilter onlineFilter = new();
    private readonly Dictionary<(string Host, int Port), ServerModCapture> onlineCaptures = [];

    private void BuildOnline()
    {
        var favorite = onlineSelectedId == "new" ? null : state.OnlineFavorites.FirstOrDefault(f => f.Id == onlineSelectedId)
            ?? state.OnlineFavorites.FirstOrDefault(f => f.ProfileId == profile.Id) ?? state.OnlineFavorites.FirstOrDefault();
        if (onlineDraft == null || favorite != null && onlineDraft.Id != favorite.Id && !onlineDirty)
        {
            onlineDraft = favorite == null ? new() : CloneFavorite(favorite);
            onlineSelectedId = favorite?.Id ?? "new";
        }
        var draft = onlineDraft!;
        var panel = new Panel { Dock = DockStyle.Fill }; pageHost.Controls.Add(panel);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 43, WrapContents = false };
        var favorites = new ComboBox { Width = 330 }; StyleCombo(favorites);
        favorites.Items.AddRange(state.OnlineFavorites.ToArray()); favorites.SelectedItem = favorite; bar.Controls.Add(favorites);
        ToolbarButton(bar, T("online.new"), 175, () =>
        {
            if (!ResolveDrafts()) return;
            onlineSelectedId = "new"; onlineDraft = new(); onlineDirty = false; onlineResult = ""; onlineTabIndex = 0;
            
            ShowPage("Online");
        });
        ToolbarButton(bar, T("online.remove"), 180, () =>
        {
            if (favorite == null || !ResolveDrafts()) return;
            state.OnlineFavorites.Remove(favorite);
            var detached = state.Profiles.FirstOrDefault(p => p.Id == favorite.ProfileId);
            if (detached != null) { detached.OnlineServerId = ""; detached.Launch.ConnectAddress = ""; detached.Launch.ConnectPassword = ""; }
            onlineSelectedId = ""; onlineDraft = null; onlineDirty = false;
            LauncherStorage.Save(state); ShowPage("Online");
        });
        var tabs = new ThemedTabs { Dock = DockStyle.Fill };
        var connection = new Panel { Text = T("online.favorites") };
        var browser = new Panel { Text = T("online.browse") };
        var serverMods = new Panel { Text = T("online.mods") };
        tabs.TabPages.Add(connection); tabs.TabPages.Add(browser); tabs.TabPages.Add(serverMods);
        panel.Controls.Add(tabs); panel.Controls.Add(bar);
        favorites.SelectedIndexChanged += (_, _) =>
        {
            if (favorites.SelectedItem is not OnlineFavorite next || next.Id == onlineSelectedId) return;
            if (!ResolveDrafts()) { favorites.SelectedItem = favorite; return; }
            onlineSelectedId = next.Id; onlineDraft = CloneFavorite(next); onlineDirty = false; onlineResult = ""; onlineTabIndex = 0; ShowPage("Online");
        };
        BuildOnlineConnection(connection, draft);
        BuildOnlineBrowser(browser, tabs);
        BuildOnlineMods(serverMods, draft);
        tabs.SelectedIndex = onlineTabIndex;
        connection.VisibleChanged += (_, _) => { if (connection.Visible) onlineTabIndex = 0; };
        browser.VisibleChanged += (_, _) => { if (browser.Visible) onlineTabIndex = 1; };
        serverMods.VisibleChanged += (_, _) => { if (serverMods.Visible) onlineTabIndex = 2; };
        UpdateFooter();
    }
    private static OnlineFavorite CloneFavorite(OnlineFavorite source)
    {
        var clone = System.Text.Json.JsonSerializer.Deserialize<OnlineFavorite>(System.Text.Json.JsonSerializer.Serialize(source))!;
        clone.Password = source.Password; return clone;
    }
    private bool SaveOnlineFavorite()
    {
        if (onlineDraft == null) return true;
        try
        {
            readOnlineModDraft?.Invoke();
            var saved = CloneFavorite(onlineDraft);
            var target = OnlineProfiles.Save(state, saved, profile, rendering ? Path.Combine(profile.CachePath, "launcher-fixtures") : null);
            if (!rendering) LauncherStorage.Save(state);
            onlineSelectedId = saved.Id; onlineDraft.ProfileId = target.Id; onlineDirty = false;
            ReloadProfileCombo(); return true;
        }
        catch (Exception ex) { if (rendering) throw; ShowError(ex); return false; }
    }
    private Panel OnlineRow(Control parent, string caption, Control input, int y, int height = 42)
    {
        var row = new Panel { Height = height, Width = Math.Max(200, parent.ClientSize.Width), Margin = Padding.Empty };
        var label = Theme.Label(caption, 9); row.Controls.Add(label); row.Controls.Add(input); parent.Controls.Add(row);
        void Arrange() { row.Width = Math.Max(200, parent.ClientSize.Width - (parent is FlowLayoutPanel ? 22 : 0)); label.SetBounds(0, 8, 165, height - 8); input.SetBounds(174, 5, Math.Max(100, row.Width - 174), height - 10); }
        row.Top = y; parent.Resize += (_, _) => Arrange(); Arrange(); return row;
    }
    private static TextBox OnlineText(string value, bool multi = false) => new()
    {
        Text = value, BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
        Multiline = multi, ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None
    };
    private void BuildOnlineConnection(Control parent, OnlineFavorite draft)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        parent.Controls.Add(flow);
        var note = Theme.Label(T("online.note"), 9, color: Theme.Muted); note.Height = 42; flow.Controls.Add(note);
        var name = OnlineText(draft.Name); name.MaxLength = 100;
        var host = OnlineText(draft.Host); host.MaxLength = 253;
        OnlineRow(flow, T("online.name"), name, 0); OnlineRow(flow, T("online.host"), host, 0);
        name.TextChanged += (_, _) => { draft.Name = name.Text; onlineDirty = true; };
        host.TextChanged += (_, _) => { draft.Host = host.Text; onlineDirty = true; };
        var ports = new Panel();
        var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Clamp(draft.Port, 1, 65535), BackColor = Theme.Surface, ForeColor = Theme.Text };
        var query = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Clamp(draft.QueryPort, 1, 65535), BackColor = Theme.Surface, ForeColor = Theme.Text };
        var queryLabel = Theme.Label(T("online.queryPort"), 9);
        ports.Controls.AddRange([port, queryLabel, query]);
        ports.Resize += (_, _) => { port.SetBounds(0, 0, 115, 28); queryLabel.SetBounds(136, 3, 150, 25); query.SetBounds(295, 0, 115, 28); };
        OnlineRow(flow, T("online.port"), ports, 0);
        port.ValueChanged += (_, _) => { int previous = draft.Port; draft.Port = (int)port.Value; if (draft.QueryPort == previous) query.Value = port.Value; onlineDirty = true; };
        query.ValueChanged += (_, _) => { draft.QueryPort = (int)query.Value; onlineDirty = true; };
        var steam = Check(T("online.steam"), draft.Steam); OnlineRow(flow, T("online.connection"), steam, 0);
        steam.CheckedChanged += (_, _) => { draft.Steam = steam.Checked; onlineDirty = true; };
        var password = OnlineText(draft.Password); password.UseSystemPasswordChar = true;
        OnlineRow(flow, T("online.password"), password, 0); tips.SetToolTip(password, T("online.passwordHelp"));
        password.TextChanged += (_, _) => { draft.Password = password.Text; onlineDirty = true; };
        var actions = new FlowLayoutPanel { Height = 45, WrapContents = false };
        flow.Controls.Add(actions);
        ToolbarButton(actions, T("online.save"), 170, () => { if (SaveOnlineFavorite()) { onlineResult = T("online.saved"); ShowPage("Online"); } });
        ToolbarButton(actions, T("online.check"), 195, async () => await CheckOnlineAsync());
        ToolbarButton(actions, T("online.join"), 170, async () => await JoinOnlineAsync(), true);
        ToolbarButton(actions, T("online.details"), 140, () =>
        {
            if (!SaveOnlineFavorite()) return;
            var info = draft.LastInfo is { } previous && previous.Host.Equals(draft.Host, StringComparison.OrdinalIgnoreCase) && previous.Port == draft.Port
                ? previous : new OnlineServerInfo { Name = draft.Name, Host = draft.Host, Port = draft.Port, QueryPort = draft.QueryPort };
            using var dialog = new OnlineServerDialog(info, state.Installation, lifetime.Token, queryOnShow: draft.Steam, steam: draft.Steam);
            dialog.ShowDialog(this); draft.LastInfo = dialog.Server;
            if (dialog.CapturedThisSession && dialog.Server.FullMods is { } captured)
            {
                draft.ModIds = dialog.Server.ModIds; draft.WorkshopIds = captured.WorkshopIds; draft.ModListConfirmed = true;
                onlineCaptures[(dialog.Server.Host, dialog.Server.Port)] = captured;
            }
            OnlineProfiles.Save(state, CloneFavorite(draft), profile); if (!rendering) LauncherStorage.Save(state);
            onlineDraft = draft; onlineDirty = false; ShowPage("Online");
        });
        var status = Theme.Label(OnlineSummary(draft), 9, color: Theme.Muted); status.Height = 120; flow.Controls.Add(status);
        flow.Resize += (_, _) => { note.Width = status.Width = actions.Width = Math.Max(200, flow.ClientSize.Width - 22); };
        note.Width = status.Width = actions.Width = Math.Max(200, flow.ClientSize.Width - 22);
    }
    private string OnlineSummary(OnlineFavorite favorite)
    {
        var lines = new List<string>();
        if (favorite.ProfileId.Length > 0) lines.Add(T("online.profileSummary", favorite.Name));
        if (favorite.LastInfo is { } info)
        {
            lines.Add(T("online.serverSummary", info.Players, info.MaxPlayers, info.Version, info.Ping, info.CheckedAt.ToString("g")));
            lines.Add(T(info.CompleteModList ? "online.completeList" : "online.partialList", info.ModIds.Count, info.ModCount?.ToString() ?? "?"));
        }
        if (onlineResult.Length > 0) lines.Add(onlineResult);
        lines.Add(T("online.versionBoundary"));
        return string.Join(Environment.NewLine, lines);
    }
    private void BuildOnlineBrowser(Control parent, ThemedTabs tabs)
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 188 };
        var note = Theme.Label(T("online.browseNote"), 9, color: Theme.Muted); note.Dock = DockStyle.Top; note.Height = 34; top.Controls.Add(note);
        var search = OnlineText(onlineFilter.Search); search.PlaceholderText = T("online.search");
        var refresh = new ActionButton(T("online.refresh")) { Name = "RefreshPublicServers", Height = 38 };
        top.Controls.Add(search); top.Controls.Add(refresh);
        top.Resize += (_, _) => { search.SetBounds(0, 43, Math.Max(180, top.Width - 250), 29); refresh.SetBounds(top.Width - 238, 38, 238, 38); };
        var filters = new TableLayoutPanel { Name = "ServerFilters", ColumnCount = 4, RowCount = 2, Height = 62 };
        var minPlayers = new NumericUpDown { Name = "MinimumServerPlayers", Minimum = 0, Maximum = 1000, Value = onlineFilter.MinimumPlayers, Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text };
        var maxPing = new NumericUpDown { Name = "MaximumServerPing", Minimum = 0, Maximum = 9999, Value = onlineFilter.MaximumPing, Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text };
        var modFilter = new ComboBox { Name = "ServerModFilter", Dock = DockStyle.Fill }; StyleCombo(modFilter);
        modFilter.Items.AddRange(new[] { T("filter.all"), T("filter.modded"), T("filter.vanilla"), T("filter.unknown") }); modFilter.SelectedIndex = onlineFilter.Mods;
        var versionFilter = OnlineText(onlineFilter.Version); versionFilter.Name = "ServerVersionFilter"; versionFilter.Dock = DockStyle.Fill; versionFilter.PlaceholderText = T("filter.allVersions");
        Control[] filterInputs = [minPlayers, modFilter, versionFilter, maxPing];
        string[] filterLabels = ["filter.players", "online.mods", "online.version", "filter.ping"];
        for (int i = 0; i < 4; i++)
        {
            filters.ColumnStyles.Add(new(SizeType.Percent, 25));
            var label = Theme.Label(T(filterLabels[i]), 9); label.Dock = DockStyle.Fill;
            filters.Controls.Add(label, i, 0); filters.Controls.Add(filterInputs[i], i, 1);
        }
        filters.RowStyles.Add(new(SizeType.Absolute, 23)); filters.RowStyles.Add(new(SizeType.Absolute, 33));
        top.Controls.Add(filters); top.Resize += (_, _) => filters.SetBounds(0, 80, top.Width, 62);
        var loading = new ProgressBar { Name = "ServerLoading", Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 28, Visible = false };
        var status = Theme.Label("", 9, color: Theme.Muted); status.Name = "ServerLoadingStatus";
        top.Controls.Add(loading); top.Controls.Add(status);
        top.Resize += (_, _) => { loading.SetBounds(0, 145, top.Width, 5); status.SetBounds(0, 157, top.Width, 27); };
        var grid = Theme.Grid(); grid.ReadOnly = true; grid.Name = "PublicServers";
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("online.name"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (var (header, width) in new[] { ("online.players", 86), ("online.mods", 80), ("online.version", 85), ("online.ping", 70), ("online.address", 175) })
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T(header), Width = width });
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.Automatic;
        grid.SortCompare += (_, e) =>
        {
            if (grid.Rows[e.RowIndex1].Tag is not OnlineServerInfo a || grid.Rows[e.RowIndex2].Tag is not OnlineServerInfo b) return;
            e.SortResult = e.Column.Index switch { 1 => a.Players.CompareTo(b.Players), 2 => Nullable.Compare(a.HasMods, b.HasMods),
                3 when Version.TryParse(a.Version, out var av) && Version.TryParse(b.Version, out var bv) => av.CompareTo(bv),
                4 => a.Ping.CompareTo(b.Ping), _ => StringComparer.OrdinalIgnoreCase.Compare(e.CellValue1?.ToString(), e.CellValue2?.ToString()) };
            if (e.SortResult == 0) e.SortResult = StringComparer.OrdinalIgnoreCase.Compare(a.Host + ":" + a.Port, b.Host + ":" + b.Port);
            e.Handled = true;
        };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, WrapContents = false };
        var save = new ActionButton(T("online.favorite")) { Width = 230, Height = 38, Primary = true };
        bottom.Controls.Add(save);
        save.Click += (_, _) =>
        {
            if (grid.CurrentRow?.Tag is not OnlineServerInfo server || !ResolveDrafts()) return;
            onlineDraft = new() { Name = server.Name.Length > 100 ? server.Name[..100] : server.Name, Host = server.Host, Port = server.Port,
                QueryPort = server.QueryPort, LastInfo = server, ModIds = server.ModIds, ModListConfirmed = server.CompleteModList,
                WorkshopIds = server.FullMods?.WorkshopIds ?? [] };
            onlineDirty = true;
            if (SaveOnlineFavorite()) { onlineResult = ""; onlineTabIndex = 0; ShowPage("Online"); }
        };
        var details = new ActionButton(T("online.details")) { Name = "ServerDetails", Width = 160, Height = 38 };
        bottom.Controls.Add(details);
        var statistics = new ActionButton(T("stats.button")) { Name = "ServerStatistics", Width = 175, Height = 38 };
        bottom.Controls.Add(statistics);
        var count = Theme.Label("", 9, color: Theme.Muted); count.Height = 40; bottom.Controls.Add(count);
        bottom.Resize += (_, _) => count.Width = Math.Max(100, bottom.Width - 590);
        parent.Controls.Add(grid); parent.Controls.Add(top); parent.Controls.Add(bottom);
        bool searching = false;
        var displayed = new Dictionary<(string, int), DataGridViewRow>();
        void RetainCapture(OnlineServerInfo server) => server.FullMods ??= onlineCaptures.GetValueOrDefault((server.Host, server.Port))
            ?? state.OnlineFavorites.FirstOrDefault(f => f.Host == server.Host && f.Port == server.Port
                && f.LastInfo?.Host == server.Host && f.LastInfo.Port == server.Port)?.LastInfo?.FullMods;
        bool Matches(OnlineServerInfo server) => onlineFilter.Matches(server);
        void Add(IEnumerable<OnlineServerInfo> servers)
        {
            foreach (var server in servers) RetainCapture(server);
            foreach (var server in servers.Where(Matches))
            {
                if (displayed.ContainsKey((server.Host, server.Port))) continue;
                int index = grid.Rows.Add(server.Name, $"{server.Players}/{server.MaxPlayers}", server.HasMods is { } hasMods ? T(hasMods ? "filter.yes" : "filter.no") : "?", server.Version, server.Ping, server.Host + ":" + server.Port);
                var row = grid.Rows[index]; row.Tag = server; displayed[(server.Host, server.Port)] = row;
                row.Cells[0].ToolTipText = server.Name + "\n" + server.Map;
            }
            if (grid.SortedColumn is { } sorted && grid.SortOrder != SortOrder.None)
                grid.Sort(sorted, grid.SortOrder == SortOrder.Ascending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending);
            count.Text = T("online.visibleResults", grid.Rows.Count, onlinePublic.Count);
            details.Enabled = grid.CurrentRow != null; save.Enabled = !searching && grid.CurrentRow != null;
        }
        void Fill()
        {
            var selected = grid.CurrentRow?.Tag as OnlineServerInfo;
            int scroll = grid.FirstDisplayedScrollingRowIndex;
            grid.Rows.Clear(); displayed.Clear(); Add(onlinePublic);
            if (selected != null && displayed.TryGetValue((selected.Host, selected.Port), out var row)) grid.CurrentCell = row.Cells[0];
            if (scroll >= 0 && grid.Rows.Count > scroll) grid.FirstDisplayedScrollingRowIndex = scroll;
        }
        statistics.Click += (_, _) =>
        {
            using var dialog = new OnlineStatisticsDialog(onlinePublic, onlinePublicPartial || searching, state.Installation, lifetime.Token);
            dialog.ShowDialog(this);
            foreach (var update in dialog.UpdatedServers)
            {
                var server = onlinePublic.FirstOrDefault(s => s.Host == update.Host && s.Port == update.Port);
                if (server != null) { server.Rules = new(update.Rules); server.Version = OnlineServerStatistics.PublicVersion(server); }
            }
            Fill();
        };
        void ShowDetails()
        {
            if (grid.CurrentRow?.Tag is not OnlineServerInfo server) return;
            using var dialog = new OnlineServerDialog(server, state.Installation, lifetime.Token);
            dialog.ShowDialog(this);
            int index = onlinePublic.IndexOf(server); if (index >= 0) onlinePublic[index] = dialog.Server;
            bool rebuild = false;
            if (dialog.CapturedThisSession && dialog.Server.FullMods != null)
            {
                onlineCaptures[(server.Host, server.Port)] = dialog.Server.FullMods;
                foreach (var saved in state.OnlineFavorites.Where(f => f.Host == server.Host && f.Port == server.Port))
                {
                    saved.LastInfo = dialog.Server; saved.ModIds = dialog.Server.ModIds;
                    saved.WorkshopIds = dialog.Server.FullMods.WorkshopIds; saved.ModListConfirmed = true;
                    if (onlineDraft?.Id == saved.Id)
                    {
                        onlineDraft.LastInfo = dialog.Server;
                        if (!onlineDirty) { onlineDraft = CloneFavorite(saved); rebuild = true; }
                    }
                }
                if (!rendering) LauncherStorage.Save(state);
            }
            Fill();
            if (rebuild && !searching) ShowPage("Online");
        }
        details.Click += (_, _) => ShowDetails();
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowDetails(); };
        grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; ShowDetails(); } };
        grid.SelectionChanged += (_, _) => { details.Enabled = grid.CurrentRow != null; save.Enabled = !searching && grid.CurrentRow != null; };
        search.TextChanged += (_, _) => { onlineFilter.Search = search.Text; Fill(); };
        minPlayers.ValueChanged += (_, _) => { onlineFilter.MinimumPlayers = (int)minPlayers.Value; Fill(); };
        maxPing.ValueChanged += (_, _) => { onlineFilter.MaximumPing = (int)maxPing.Value; Fill(); };
        modFilter.SelectedIndexChanged += (_, _) => { onlineFilter.Mods = modFilter.SelectedIndex; Fill(); };
        versionFilter.TextChanged += (_, _) => { onlineFilter.Version = versionFilter.Text.Trim(); Fill(); }; Fill();
        status.Text = T(onlinePublicPartial ? "online.resultsPartial" : "online.results", onlinePublic.Count);
        refresh.Click += async (_, _) =>
        {
            if (searching) { onlineCancellation?.Cancel(); return; }
            if (onlineBusy || workshopBusy || worldBusy || !ResolveDrafts()) return;
            searching = true; tabs.SelectionLocked = true; save.Enabled = false;
            loading.Visible = true; refresh.Text = T("dialog.cancel"); status.Text = T("online.searchStarting");
            onlinePublic = []; onlinePublicPartial = true; onlineResult = ""; Fill();
            var received = new HashSet<(string, int)>();
            try
            {
                await RunOnlineAsync(async token =>
                {
                    var result = await SteamServerBrowser.RunAsync(state.Installation, null, token, batch =>
                    {
                        if (grid.IsDisposed) return;
                        var added = batch.Servers.Where(s => received.Add((s.Host, s.Port))).ToList();
                        onlinePublic.AddRange(added); Add(added);
                        status.Text = T("online.searchProgress", onlinePublic.Count, batch.Discovered, batch.ElapsedSeconds);
                    });
                    result.Servers.ForEach(RetainCapture);
                    onlinePublic = result.Servers; onlinePublicPartial = result.Partial;
                }, parent);
            }
            finally
            {
                searching = false; tabs.SelectionLocked = false;
                if (!grid.IsDisposed)
                {
                    loading.Visible = false; refresh.Text = T("online.refresh"); Fill();
                    status.Text = onlineResult.Length > 0 ? onlineResult + " " + T("online.resultsPartial", onlinePublic.Count)
                        : T(onlinePublicPartial ? "online.resultsPartial" : "online.results", onlinePublic.Count);
                }
            }
        };
    }
    private void BuildOnlineMods(Control parent, OnlineFavorite draft)
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 239 };
        var note = Theme.Label(T("online.modsNote"), 9, color: Theme.Muted); note.SetBounds(0, 0, 700, 42); top.Controls.Add(note);
        var ids = OnlineText(string.Join("; ", draft.WorkshopIds), true);
        var modsInput = OnlineText(string.Join("; ", draft.ModIds), true);
        ids.Name = "OnlineWorkshopIds"; modsInput.Name = "OnlineModIds";
        var row1 = OnlineRow(top, T("online.workshopIds"), ids, 46, 54);
        var row2 = OnlineRow(top, T("online.modIds"), modsInput, 102, 54);
        
        var actions = new FlowLayoutPanel { Height = 44, WrapContents = false }; top.Controls.Add(actions);
        var confirmed = Check(T("online.confirmList"), draft.ModListConfirmed); top.Controls.Add(confirmed);
        ids.TextChanged += (_, _) => onlineDirty = true; modsInput.TextChanged += (_, _) => onlineDirty = true;
        confirmed.CheckedChanged += (_, _) => { draft.ModListConfirmed = confirmed.Checked; onlineDirty = true; };
        bool Capture()
        {
            try
            {
                draft.WorkshopIds = string.IsNullOrWhiteSpace(ids.Text) ? [] : WorkshopStore.ParseIds(ids.Text);
                draft.ModIds = OnlineProfiles.ParseModIds(modsInput.Text); return SaveOnlineFavorite();
            }
            catch (Exception ex) { ShowError(ex); return false; }
        }
        
        readOnlineModDraft = () =>
        {
            if (!ReferenceEquals(draft, onlineDraft)) return;
            draft.WorkshopIds = string.IsNullOrWhiteSpace(ids.Text) ? [] : WorkshopStore.ParseIds(ids.Text);
            draft.ModIds = OnlineProfiles.ParseModIds(modsInput.Text);
        };
        parent.Disposed += (_, _) => readOnlineModDraft = null;
        ToolbarButton(actions, T("online.identify"), 165, () =>
        {
            try
            {
                var wanted = OnlineProfiles.ParseModIds(modsInput.Text);
                if (wanted.Count == 0 && draft.LastInfo is { } info) wanted = info.ModIds;
                var catalog = OnlineMods.SteamCatalog(InstallationLocator.ReadGameVersion(state.Installation));
                modsInput.Text = string.Join("; ", wanted);
                var existing = string.IsNullOrWhiteSpace(ids.Text) ? [] : WorkshopStore.ParseIds(ids.Text);
                ids.Text = string.Join("; ", existing.Concat(draft.LastInfo?.FullMods?.WorkshopIds ?? []).Concat(OnlineMods.IdentifyWorkshop(wanted, catalog)).Distinct());
                if (draft.LastInfo?.CompleteModList == true && wanted.SequenceEqual(draft.LastInfo.ModIds)) confirmed.Checked = true;
            }
            catch (Exception ex) { ShowError(ex); }
        });
        ToolbarButton(actions, T("online.importIni"), 170, () =>
        {
            using var dialog = new OpenFileDialog { Filter = "Server INI (*.ini)|*.ini" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 4_000_000) throw new InvalidDataException(T("online.iniInvalid"));
                var parsed = OnlineProfiles.ParseServerIni(File.ReadAllText(dialog.FileName));
                ids.Text = string.Join("; ", parsed.Workshop); modsInput.Text = string.Join("; ", parsed.Mods); confirmed.Checked = true;
            }
            catch (Exception ex) { ShowError(ex); }
        });
        ToolbarButton(actions, T("online.save"), 150, () => { if (Capture()) { onlineResult = T("online.saved"); ShowPage("Online"); } });
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, WrapContents = false };
        ToolbarButton(bottom, T("online.copy"), 190, async () =>
        {
            if (!Capture()) return;
            await RunOnlineAsync(async token =>
            {
                var target = state.Profiles.Single(p => p.Id == draft.ProfileId);
                var result = await OnlineMods.CopyInstalledAsync(draft, target, token);
                onlineResult = string.Join("\n", result.Select(r => r.Id + " · " + r.Detail));
            });
            if (!IsDisposed) ShowPage("Online");
        });
        ToolbarButton(bottom, T("online.updates"), 190, async () => { if (Capture()) { await CheckOnlineUpdatesAsync(); if (!IsDisposed) ShowPage("Online"); } });
        ToolbarButton(bottom, T("online.download"), 235, () =>
        {
            if (!Capture() || !ResolveDrafts()) return;
            SelectOnlineProfile(draft);
            workshopProfileId = profile.Id; workshopDraft = string.Join(Environment.NewLine, draft.WorkshopIds);
            ShowPage("Mods");
            Descendants(pageHost).OfType<ThemedTabs>().First().SelectedIndex = 2;
        }, true);
        var output = OnlineText(onlineResult.Length > 0 ? onlineResult : T("online.modsHelp"), true);
        output.ReadOnly = true; output.Dock = DockStyle.Fill;
        parent.Controls.Add(output); parent.Controls.Add(top); parent.Controls.Add(bottom);
        top.Resize += (_, _) =>
        {
            note.Width = top.Width;
            actions.SetBounds(0, 159, top.Width, 44); confirmed.SetBounds(0, 204, top.Width, 30);
        };
    }
    private Action? readOnlineModDraft;
    private void SelectOnlineProfile(OnlineFavorite favorite)
    {
        profile = state.Profiles.Single(p => p.Id == favorite.ProfileId);
        OnlineProfiles.ApplyConnection(profile, favorite);
        state.SelectedProfile = profile.Id; selection = new(profile.CachePath); modsDirty = false;
        mods = []; saves = []; ReloadProfileCombo();
        if (!rendering) LauncherStorage.Save(state);
        _ = RefreshLibraryAsync();
    }
    private async Task CheckOnlineAsync()
    {
        if (!SaveOnlineFavorite() || onlineDraft == null) return;
        var draft = onlineDraft;
        await RunOnlineAsync(async token =>
        {
            var result = await SteamServerBrowser.RunAsync(state.Installation, draft, token);
            var info = result.Servers.Single();
            info.FullMods = draft.LastInfo?.FullMods; draft.LastInfo = info;
            if (!draft.ModListConfirmed && info.CompleteModList) { draft.ModIds = info.ModIds; draft.ModListConfirmed = true; }
            var catalog = OnlineMods.SteamCatalog(InstallationLocator.ReadGameVersion(state.Installation));
            draft.WorkshopIds = draft.WorkshopIds.Concat(OnlineMods.IdentifyWorkshop(draft.ModIds.Count > 0 ? draft.ModIds : info.ModIds, catalog)).Distinct().ToList();
            OnlineProfiles.Save(state, CloneFavorite(draft), profile); if (!rendering) LauncherStorage.Save(state);
            onlineResult = T("online.checked");
            await CheckOnlineUpdatesCoreAsync(draft, token);
        });
        if (!IsDisposed) ShowPage("Online");
    }
    private async Task CheckOnlineUpdatesAsync()
    {
        if (onlineDraft == null) return;
        await RunOnlineAsync(token => CheckOnlineUpdatesCoreAsync(onlineDraft, token));
    }
    private async Task CheckOnlineUpdatesCoreAsync(OnlineFavorite draft, CancellationToken token)
    {
        var published = await OnlineMods.PublishedAsync(draft.WorkshopIds, token);
        var target = state.Profiles.Single(p => p.Id == draft.ProfileId);
        var status = OnlineMods.Compare(draft, target.CachePath, published);
        var available = ModCatalog.Scan(target, InstallationLocator.ReadGameVersion(state.Installation)).Where(m => m.Available).Select(m => m.Id).ToHashSet();
        var missing = draft.ModIds.Where(id => !available.Contains(id)).ToList();
        string Date(long time) => time > 0 ? DateTimeOffset.FromUnixTimeSeconds(time).ToLocalTime().ToString("g") : "?";
        onlineResult = T("online.checkedAt", DateTimeOffset.Now.ToString("g")) + "\n" +
            (missing.Count == 0 ? "" : T("online.missingBeforeJoin", string.Join(", ", missing.Take(20))) + "\n") +
            string.Join("\n", status.Select(s => $"{s.Id} · {s.Title} · {T("online.mod." + s.Status)}\r\n  {T("online.revision", Date(s.Installed), Date(s.Published), s.Manifest.Length == 0 ? "?" : s.Manifest)}")) +
            "\n" + T("online.versionBoundary");
    }
    private async Task JoinOnlineAsync()
    {
        if (!SaveOnlineFavorite() || onlineDraft == null || !ResolveDrafts()) return;
        SelectOnlineProfile(onlineDraft);
        await RefreshLibraryAsync();
        if (!IsDisposed) LaunchGame();
    }

    private void ValidateOnlineLaunch()
    {
        if (profile.OnlineServerId.Length == 0) return;
        var favorite = state.OnlineFavorites.SingleOrDefault(f => f.Id == profile.OnlineServerId && f.ProfileId == profile.Id)
            ?? throw new InvalidDataException(T("online.profileConflict"));
        OnlineProfiles.Validate(favorite);
        if (favorite.Steam && InstallationLocator.IsGog(state.Installation)) throw new InvalidOperationException(T("install.steamNeeded"));
        if (state.Profiles.Any(p => p.Id != profile.Id && Path.GetFullPath(p.CachePath).Equals(Path.GetFullPath(profile.CachePath), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(T("online.profileConflict"));
        OnlineProfiles.ApplyConnection(profile, favorite);
        var installed = ModCatalog.Scan(profile, InstallationLocator.ReadGameVersion(state.Installation));
        var missing = favorite.ModIds.Where(id => !installed.Any(m => m.Id == id && m.Available)).ToList();
        var tracked = WorkshopStore.Read(profile.CachePath);
        var missingItems = favorite.WorkshopIds.Where(id => !tracked.Any(i => i.ItemId == id && i.Mods.Count > 0 && i.Mods.All(m => Directory.Exists(Path.Combine(profile.CachePath, "mods", m.Folder))))).ToList();
        if (missing.Count > 0 || missingItems.Count > 0) throw new InvalidDataException(T("online.missingBeforeJoin", string.Join(", ", missing.Concat(missingItems).Take(20))));
        var local = InstallationLocator.ReadGameVersion(state.Installation);
        if (local != null && Version.TryParse(favorite.LastInfo?.Version, out var remote) && !OnlineProfiles.SameVersion(local, remote))
            throw new InvalidDataException(T("online.versionMismatch", local, remote));
    }
    private async Task RunOnlineAsync(Func<CancellationToken, Task> action, Control? interactive = null)
    {
        if (onlineBusy || workshopBusy || worldBusy) return;
        onlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        onlineBusy = true;
        var disabled = new Dictionary<Control, bool>();
        void Disable(Control control) { disabled.TryAdd(control, control.Enabled); control.Enabled = false; }
        if (interactive == null) { Disable(pageHost); Disable(profileCombo); Disable(languageCombo); foreach (var nav in navigation.Values) Disable(nav); }
        else
            for (Control branch = interactive; branch.Parent is Control parent && parent != this; branch = parent)
                foreach (Control sibling in parent.Controls) if (sibling != branch) Disable(sibling);
        using var cancel = interactive == null ? new ActionButton(T("dialog.cancel")) { Dock = DockStyle.Bottom, Height = 40 } : null;
        if (cancel != null)
        {
            cancel.Click += (_, _) => onlineCancellation?.Cancel();
            pageHost.Parent!.Controls.Add(cancel); cancel.BringToFront();
        }
        UpdateFooter(); footerDetail.Text = T(interactive == null ? "online.working" : "online.searchStarting");
        try { await action(onlineCancellation.Token); }
        catch (OperationCanceledException) { onlineResult = T("online.cancelled"); }
        catch (Exception ex) { onlineResult = ex.Message; if (!IsDisposed) ShowError(ex); }
        finally
        {
            onlineCancellation.Dispose(); onlineCancellation = null; onlineBusy = false; cancel?.Dispose();
            if (!IsDisposed)
            {
                foreach (var item in disabled) if (!item.Key.IsDisposed) item.Key.Enabled = item.Value;
                UpdateFooter();
                if (closeAfterOnline) { closeAfterOnline = false; Close(); }
            }
        }
    }
}
