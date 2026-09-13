using System.ComponentModel;

namespace PZLauncher.Desktop;

internal sealed class OnlineStatisticsDialog : Form
{
    private readonly List<OnlineServerInfo> servers;
    private readonly Dictionary<(string Host, int Port), OnlineServerInfo> updates = [];
    private readonly Label scope = Theme.Label("", 9, color: Theme.Muted);
    private readonly Label modsSummary = Theme.Label("", 12);
    private readonly Label activitySummary = Theme.Label("", 12);
    private readonly Label coverage = Theme.Label("", 9, color: Theme.Muted);
    private readonly Label status = Theme.Label("", 9, color: Theme.Muted);
    private readonly DataGridView versions = Theme.Grid(), topMods = Theme.Grid();
    private readonly ActionButton collect = new(T("stats.collect"));
    private readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 5 };
    private readonly bool partial;
    private readonly DateTimeOffset openedAt = DateTimeOffset.Now;
    private readonly Func<IReadOnlyList<SteamRulesTarget>, Action<SteamBrowserProgress>, CancellationToken, Task<SteamBrowserResult>> query;
    private readonly CancellationToken lifetime;
    private CancellationTokenSource? operation;
    private bool closePending;
    internal IReadOnlyCollection<OnlineServerInfo> UpdatedServers => updates.Values;
    internal Task CollectionTask { get; private set; } = Task.CompletedTask;

    internal OnlineStatisticsDialog(IEnumerable<OnlineServerInfo> source, bool partial, string installation, CancellationToken lifetime,
        Func<IReadOnlyList<SteamRulesTarget>, Action<SteamBrowserProgress>, CancellationToken, Task<SteamBrowserResult>>? query = null)
    {
        this.partial = partial; this.lifetime = lifetime;
        this.query = query ?? ((targets, report, token) => SteamServerBrowser.RunAsync(installation, null, token, report, targets));
        servers = OnlineServerStatistics.Unique(source).Select(s => new OnlineServerInfo
        {
            Host = s.Host, Port = s.Port, QueryPort = s.QueryPort, Name = s.Name, Version = s.Version,
            Players = s.Players, MaxPlayers = s.MaxPlayers, Tags = s.Tags, Responded = true,
            CheckedAt = s.CheckedAt, Rules = new(s.Rules)
        }).ToList();
        Name = "OnlineStatistics"; Text = T("stats.title"); Font = Theme.Font();
        BackColor = Theme.Background; ForeColor = Theme.Text; AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable; SizeGripStyle = SizeGripStyle.Show;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
        ClientSize = new(1000, 740); MinimumSize = new(890, 650); Padding = new(22);
        var heading = Theme.Label(T("stats.title"), 20, true); heading.Dock = DockStyle.Top; heading.Height = 40;
        scope.Name = "StatisticsScope"; scope.Dock = DockStyle.Top; scope.Height = 44;
        var summary = new TableLayoutPanel { Dock = DockStyle.Top, Height = 86, ColumnCount = 2 };
        summary.ColumnStyles.Add(new(SizeType.Percent, 50)); summary.ColumnStyles.Add(new(SizeType.Percent, 50));
        modsSummary.Name = "StatisticsMods"; activitySummary.Name = "StatisticsActivity";
        foreach (var label in new[] { modsSummary, activitySummary })
        { label.Dock = DockStyle.Fill; label.Padding = new(12, 9, 10, 6); label.BackColor = Theme.Surface; label.Margin = new(0, 0, 10, 14); summary.Controls.Add(label); }
        var tabs = new ThemedTabs { Name = "StatisticsTabs", Dock = DockStyle.Fill };
        var versionsPage = new Panel { Text = T("stats.versions") };
        var modsPage = new Panel { Text = T("stats.topMods") };
        tabs.TabPages.Add(versionsPage); tabs.TabPages.Add(modsPage);
        versions.Name = "StatisticsVersions"; topMods.Name = "StatisticsTopMods";
        versions.ReadOnly = topMods.ReadOnly = true;
        versions.RowTemplate.Height = topMods.RowTemplate.Height = 32;
        versions.ColumnHeadersHeight = topMods.ColumnHeadersHeight = 36;
        void Column(DataGridView grid, string key, int width, bool fill = false, string? format = null)
        {
            var column = new DataGridViewTextBoxColumn { HeaderText = T(key), Width = width,
                SortMode = DataGridViewColumnSortMode.Automatic, AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None };
            if (format != null) column.DefaultCellStyle.Format = format;
            grid.Columns.Add(column);
        }
        Column(versions, "online.version", 200, true); Column(versions, "stats.servers", 115);
        Column(versions, "stats.share", 140, format: "0.0' %'"); Column(versions, "stats.occupied", 150); Column(versions, "online.players", 100);
        Column(topMods, "stats.rank", 70); Column(topMods, "stats.modId", 250, true);
        Column(topMods, "stats.servers", 115); Column(topMods, "stats.share", 140, format: "0.0' %'");
        coverage.Name = "StatisticsCoverage"; coverage.Dock = DockStyle.Top; coverage.AutoEllipsis = false;
        modsPage.Resize += (_, _) => ArrangeCoverage();
        versionsPage.Controls.Add(versions); modsPage.Controls.Add(topMods); modsPage.Controls.Add(coverage);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 78 };
        var actions = new Panel { Dock = DockStyle.Bottom, Height = 42 };
        collect.Name = "CollectPublicMods"; collect.Width = 320; collect.Dock = DockStyle.Left;
        var close = new ActionButton(T("dialog.close")) { Width = 130, Dock = DockStyle.Right };
        close.Click += (_, _) => Close(); CancelButton = close;
        actions.Controls.Add(collect); actions.Controls.Add(close);
        status.Name = "StatisticsCollectionStatus"; status.Dock = DockStyle.Fill; status.Padding = new(0, 7, 0, 0);
        bottom.Controls.Add(status); bottom.Controls.Add(progress); bottom.Controls.Add(actions);
        Controls.Add(tabs); Controls.Add(summary); Controls.Add(scope); Controls.Add(heading); Controls.Add(bottom);
        collect.Click += (_, _) => { if (operation != null) operation.Cancel(); else CollectionTask = CollectAsync(); };
        FormClosing += (_, e) => { if (operation == null) return; e.Cancel = true; closePending = true; operation.Cancel(); };
        Fill(); status.Text = T("stats.ready");
    }
    private void Fill()
    {
        var summary = OnlineServerStatistics.Build(servers);
        scope.Text = T("stats.scope", summary.Total, openedAt.ToString("g")) + " " + T(partial ? "stats.partial" : "stats.sample");
        modsSummary.Text = T("stats.modsSummary", summary.Vanilla, summary.Modded, summary.UnknownMods);
        activitySummary.Text = T("stats.activitySummary", summary.Occupied, summary.Empty, summary.Players, summary.UnknownPlayers);
        coverage.Text = T("stats.coverage", summary.PublicLists, summary.Total, summary.CompleteLists, summary.PublicLists - summary.CompleteLists)
            + "\r\n" + T("stats.rankingNote") + (summary.TopMods.Count == 0 ? "\r\n" + T("stats.noMods") : "");
        ArrangeCoverage();
        decimal Share(int count) => summary.Total == 0 ? 0 : 100m * count / summary.Total;
        void Rows(DataGridView grid, Action fill)
        {
            int scroll = grid.FirstDisplayedScrollingRowIndex;
            var sort = grid.SortedColumn; var direction = grid.SortOrder;
            grid.Rows.Clear(); fill();
            if (sort != null && direction != SortOrder.None) grid.Sort(sort,
                direction == SortOrder.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending);
            if (scroll >= 0 && scroll < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = scroll;
        }
        Rows(versions, () => { foreach (var version in summary.Versions)
            versions.Rows.Add(version.Version.Length == 0 ? T("filter.unknown") : version.Version, version.Servers, Share(version.Servers), version.Occupied, version.Players); });
        Rows(topMods, () => { int rank = 0; foreach (var mod in summary.TopMods) topMods.Rows.Add(++rank, mod.Id, mod.Servers, Share(mod.Servers)); });
        if (operation == null) collect.Enabled = servers.Any(s => OnlineServerStatistics.PublicHasMods(s) != false);
    }
    private void Merge(IEnumerable<OnlineServerInfo> responses)
    {
        var lookup = servers.ToDictionary(s => (s.Host, s.Port));
        bool changed = false;
        foreach (var response in responses)
        {
            if (!lookup.TryGetValue((response.Host, response.Port), out var server)) continue;
            server.Rules = new(response.Rules); updates[(response.Host, response.Port)] = response;
            changed = true;
        }
        if (changed) Fill();
    }
    private void ArrangeCoverage() => coverage.Height = Math.Max(48, TextRenderer.MeasureText(coverage.Text, coverage.Font,
        new Size(Math.Max(400, coverage.Width), 0), TextFormatFlags.WordBreak).Height + 8);
    private async Task CollectAsync()
    {
        var targets = servers.Where(s => OnlineServerStatistics.PublicHasMods(s) != false)
            .Select(s => new SteamRulesTarget(s.Host, s.Port, s.QueryPort)).ToList();
        if (targets.Count == 0) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime); operation = cancellation;
        collect.Text = T("dialog.cancel"); progress.Maximum = targets.Count; progress.Value = 0;
        status.Text = T("stats.collecting", 0, targets.Count, 0, 0);
        try
        {
            var result = await query(targets, batch =>
            {
                if (IsDisposed) return;
                Merge(batch.Servers); progress.Value = Math.Clamp(batch.Completed, 0, targets.Count);
                status.Text = T("stats.collecting", batch.Completed, targets.Count, batch.Failed, batch.ElapsedSeconds);
            }, cancellation.Token);
            Merge(result.Servers);
            status.Text = T(result.Partial ? "stats.collectionPartial" : "stats.collected", result.Servers.Count, targets.Count);
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = T("stats.cancelled"); }
        catch (Exception ex) { if (!IsDisposed) status.Text = T("stats.failed", ex.Message); }
        finally
        {
            operation = null;
            if (!IsDisposed) { collect.Text = T("stats.collect"); Fill(); if (closePending) Close(); }
        }
    }
}
