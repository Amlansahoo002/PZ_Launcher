namespace PZLauncher.Desktop;

internal sealed class OnlineServerDialog : Form
{
    private OnlineServerInfo server;
    private readonly string installation;
    private readonly CancellationToken lifetime;
    private CancellationTokenSource? request;
    private readonly Label heading = Theme.Label("", 17, true);
    private readonly Label summary = Theme.Label("", 10);
    private readonly Label status = Theme.Label("", 9, color: Theme.Muted);
    private readonly ProgressBar loading = new() { Dock = DockStyle.Bottom, Height = 5, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly ActionButton refresh = new(T("online.detailsRefresh")) { Width = 200, Height = 38, Dock = DockStyle.Left };
    private readonly TextBox description = ReadOnlyText("ServerDescription"), mods = ReadOnlyText("ServerMods");
    private readonly DataGridView rules = Theme.Grid();
    private readonly ActionButton grab = new(T("probe.button")) { Name = "GrabAllMods", Width = 240, Height = 38, Dock = DockStyle.Left };
    internal OnlineServerInfo Server => server;
    internal bool CapturedThisSession { get; private set; }

    internal OnlineServerDialog(OnlineServerInfo server, string installation, CancellationToken lifetime, bool queryOnShow = true, bool steam = true)
    {
        this.server = server; this.installation = installation; this.lifetime = lifetime;
        Text = T("online.details"); Name = "ServerDetailsDialog";
        Font = Theme.Font(); BackColor = Theme.Background; ForeColor = Theme.Text;
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(850, 620); MinimumSize = new Size(690, 530); Padding = new Padding(18);
        FormBorderStyle = FormBorderStyle.Sizable; SizeGripStyle = SizeGripStyle.Show;
        ShowInTaskbar = false; MinimizeBox = false; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        heading.Dock = DockStyle.Top; heading.Height = 44;
        summary.Dock = DockStyle.Top; summary.Height = 140; summary.Name = "ServerSummary";
        status.Dock = DockStyle.Bottom; status.Height = 55; status.Name = "ServerDetailsStatus";
        var tabs = new ThemedTabs { Dock = DockStyle.Fill };
        var descriptionPage = new Panel { Text = T("online.description") };
        var modsPage = new Panel { Text = T("online.mods") };
        var rulesPage = new Panel { Text = T("online.rules") };
        descriptionPage.Controls.Add(description); modsPage.Controls.Add(mods);
        rules.ReadOnly = true; rules.Name = "ServerRules";
        rules.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("online.rule"), Width = 220 });
        rules.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("online.value"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        rulesPage.Controls.Add(rules);
        tabs.TabPages.Add(descriptionPage); tabs.TabPages.Add(modsPage); tabs.TabPages.Add(rulesPage);
        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var close = new ActionButton(T("dialog.close")) { Width = 130, Height = 38, Dock = DockStyle.Right, DialogResult = DialogResult.Cancel };
        refresh.Width = 185; close.Width = 110;
        buttons.Controls.Add(grab); buttons.Controls.Add(refresh); buttons.Controls.Add(close); CancelButton = close;
        Controls.Add(tabs); Controls.Add(summary); Controls.Add(heading); Controls.Add(status); Controls.Add(loading); Controls.Add(buttons);
        refresh.Click += async (_, _) => { if (request != null) request.Cancel(); else await RefreshServerAsync(); };
        grab.Click += (_, _) =>
        {
            using var dialog = new ServerProbeDialog(installation, server, lifetime, steam);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ModCapture == null) return;
            server.FullMods = dialog.ModCapture; CapturedThisSession = true; Fill();
            tabs.SelectedIndex = 1;
            status.Text = T("probe.captured", dialog.ModCapture.Mods.Count, dialog.ModCapture.Workshop.Count,
                dialog.ModCapture.CapturedAt.ToLocalTime().ToString("g"));
        };
        Shown += async (_, _) => { refresh.Select(); if (queryOnShow) await RefreshServerAsync(); };
        FormClosing += (_, _) => request?.Cancel();
        Fill(); status.Text = T(server.Rules.Count > 0 ? "online.detailsReady" : "online.detailsCached");
    }
    private static TextBox ReadOnlyText(string name) => new()
    {
        Name = name, Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
        BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle
    };
    private void Fill()
    {
        heading.Text = server.Name;
        summary.Text = $"{T("online.address")} : {server.Host}:{server.Port}    ·    {T("online.queryPort")} : {server.QueryPort}\r\n" +
            $"{T("online.players")} : {server.Players}/{server.MaxPlayers}    ·    {T("online.ping")} : {server.Ping} ms\r\n" +
            $"{T("online.version")} : {server.Version}    ·    {T("online.map")} : {server.Map}\r\n" +
            $"{T("online.password")} : {T(server.PasswordProtected ? "online.required" : "online.notRequired")}\r\n" +
            T("online.serverCheckedAt", server.CheckedAt.ToLocalTime().ToString("g"));
        description.Text = server.Rules.GetValueOrDefault("description", T("online.noDescription"));
        try
        {
            mods.Text = T(server.CompleteModList ? "online.completeList" : "online.partialList", server.ModIds.Count, server.ModCount?.ToString() ?? "?")
                + "\r\n\r\n" + string.Join("\r\n", server.ModIds);
            if (server.FullMods is { } capture)
                mods.Text = T("probe.captured", capture.Mods.Count, capture.Workshop.Count, capture.CapturedAt.ToLocalTime().ToString("g"))
                    + "\r\n" + T("probe.snapshot", capture.GameVersion)
                    + "\r\n\r\n" + string.Join("\r\n", capture.Mods.Select(m => $"{m.Name}  [{m.Id}]" + (m.WorkshopId.Length > 0 ? "  ·  Workshop " + m.WorkshopId : "")))
                    + "\r\n\r\nWorkshop\r\n" + string.Join("\r\n", capture.Workshop.Select(w => w.Id + "  ·  " + T("probe.timestamp", w.Updated)));
        }
        catch (InvalidDataException) { mods.Text = T("online.invalidModList"); }
        rules.Rows.Clear();
        foreach (var pair in server.Rules.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)) rules.Rows.Add(pair.Key, pair.Value);
    }
    private async Task RefreshServerAsync()
    {
        if (request != null || IsDisposed) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        request = cancellation; grab.Enabled = false; loading.Visible = true; refresh.Text = T("dialog.cancel"); status.Text = T("online.detailsLoading");
        try
        {
            var result = await SteamServerBrowser.RunAsync(installation,
                new OnlineFavorite { Host = server.Host, QueryPort = server.QueryPort }, cancellation.Token);
            if (IsDisposed) return;
            var next = result.Servers.Single(); next.FullMods = server.FullMods;
            server = next; Fill();
            status.Text = T(result.Partial ? "online.detailsPartial" : "online.detailsReady");
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = T("online.cancelled"); }
        catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
        finally
        {
            request = null;
            if (!IsDisposed) { loading.Visible = false; grab.Enabled = true; refresh.Text = T("online.detailsRefresh"); }
        }
    }
}
