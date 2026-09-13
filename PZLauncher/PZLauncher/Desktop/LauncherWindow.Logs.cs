namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private string diagnosticPath = "";
    private Task diagnosticTask = Task.CompletedTask;
    private void BuildDiagnostics()
    {
        var panel = new Panel { Dock = DockStyle.Fill }; pageHost.Controls.Add(panel);
        var top = new ResponsiveToolbar { Dock = DockStyle.Top, Height = 83, Name = "DiagnosticActions" };
        var path = new TextBox { Width = 470, BackColor = Theme.Surface, ForeColor = Theme.Text, ReadOnly = true };
        if (diagnosticPath.Length == 0) diagnosticPath = Path.Combine(profile.CachePath, "console.txt"); path.Text = diagnosticPath; top.Controls.Add(path);
        var search = new TextBox { Width = 235, BackColor = Theme.Surface, ForeColor = Theme.Text, PlaceholderText = T("log.search") };
        var level = new ComboBox { Width = 140 }; StyleCombo(level); level.Items.AddRange([T("log.allLevels"), "ERROR", "WARN"]); level.SelectedIndex = 0;
        var status = Theme.Label("", 8, color: Theme.Muted); status.Width = 380; status.Height = 30;
        var details = Readout(T("log.help")); details.Height = 185; details.Dock = DockStyle.Bottom;
        var grid = Theme.Grid(); grid.RowTemplate.Height = 31; grid.ReadOnly = true;
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("log.line"), Width = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("log.level"), Width = 85 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "×", Width = 50 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("log.problem"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        var views = new ThemedTabs { Dock = DockStyle.Fill };
        var issuesPage = new Panel { Text = T("log.issues") }; issuesPage.Controls.Add(grid); issuesPage.Controls.Add(details);
        var sourcePage = new Panel { Text = T("log.source") };
        var raw = new RichTextBox { Name = "DiagnosticSource", Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, WordWrap = false, Font = new Font("Consolas", 9.5f), BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, HideSelection = false };
        var markers = new LogMarkers { Name = "DiagnosticMarkers" };
        var navigation = new ResponsiveToolbar { Dock = DockStyle.Bottom };
        var position = Theme.Label("", 9, color: Theme.Muted); position.Width = 210; position.Height = 35;
        sourcePage.Controls.Add(raw); sourcePage.Controls.Add(markers); sourcePage.Controls.Add(navigation);
        views.TabPages.Add(issuesPage); views.TabPages.Add(sourcePage);
        panel.Controls.Add(views); panel.Controls.Add(top);
        LogReport? report = null; int loadRevision = 0;
        int selectedLine = 0;
        void Jump(int line)
        {
            if (report == null) return;
            views.SelectedIndex = 1; int start = raw.GetFirstCharIndexFromLine(line - 1); if (start < 0) return;
            int next = raw.GetFirstCharIndexFromLine(line);
            raw.Select(start, (next < 0 ? raw.TextLength : next) - start); raw.ScrollToCaret(); raw.Focus();
            selectedLine = line; markers.SelectedLine = line; markers.Invalidate(); position.Text = T("log.position", line, markers.TotalLines);
        }
        void Move(int direction)
        {
            var lines = markers.Marks.Where(m => m.Level == "ERROR").Select(m => m.Line).Order().ToArray(); if (lines.Length == 0) return;
            Jump(direction > 0 ? lines.FirstOrDefault(l => l > selectedLine, lines[0]) : lines.LastOrDefault(l => l < selectedLine, lines[^1]));
        }
        markers.Selected += Jump;
        ToolbarButton(navigation, T("log.previous"), 210, () => Move(-1));
        ToolbarButton(navigation, T("log.next"), 210, () => Move(1)); navigation.Controls.Add(position);
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is LogIssue issue) Jump(issue.Line); };
        grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && grid.CurrentRow?.Tag is LogIssue issue) { Jump(issue.Line); e.Handled = true; } };
        void ShowDetails()
        {
            details.Text = grid.CurrentRow?.Tag is LogIssue issue
                ? issue.Explanation + "\r\n\r\n" + issue.Attribution + "\r\n" + T("log.evidence") + "\r\n\r\n" + issue.Evidence : T("log.help");
        }
        void Fill()
        {
            grid.Rows.Clear();
            if (report == null) return;
            foreach (var issue in report.Issues.Where(i => (level.SelectedIndex == 0 || i.Level == level.Text) &&
                (i.Evidence.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || i.Explanation.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) || i.Attribution.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase))))
            {
                int row = grid.Rows.Add(issue.Line, issue.Level, issue.Count, issue.Summary); grid.Rows[row].Tag = issue;
            }
            status.Text = T("log.count", report.Issues.Count) + (report.TailOnly ? " · " + T("log.tail") : "");
            markers.Marks = report.Issues.Where(i => level.SelectedIndex == 0 || i.Level == level.Text).SelectMany(i => i.Lines.Select(l => (l, i.Level))).ToList(); markers.Invalidate();
            ShowDetails();
        }
        async Task Load()
        {
            int revision = ++loadRevision;
            try
            {
                string file = diagnosticPath; path.Text = file; tips.SetToolTip(path, file);
                if (!File.Exists(file)) { report = null; raw.Clear(); markers.Marks = []; markers.Invalidate(); grid.Rows.Clear(); ShowDetails(); status.Text = T("log.missing"); return; }
                var catalog = mods.ToArray(); var next = await Task.Run(() => ConsoleDiagnostics.Read(file, catalog));
                if (!panel.IsDisposed && revision == loadRevision) { report = next; raw.Text = next.Text.Replace("\r\n", "\n"); markers.TotalLines = raw.Text.Count(c => c == '\n') + 1; selectedLine = 0; markers.SelectedLine = 0; Fill(); }
            }
            catch (Exception ex) { if (!panel.IsDisposed) ShowError(ex); }
        }
        ToolbarButton(top, T("db.open"), 130, () =>
        {
            using var file = new OpenFileDialog { Filter = "PZ logs (*.txt;*.log)|*.txt;*.log", InitialDirectory = profile.CachePath };
            if (file.ShowDialog(this) == DialogResult.OK) { diagnosticPath = file.FileName; diagnosticTask = Load(); }
        });
        ToolbarButton(top, T("refresh"), 140, () => diagnosticTask = Load());
        top.SetFlowBreak(top.Controls[^1], true); top.Controls.AddRange([search, level, status]);
        top.SizeChanged += (_, _) => { path.Width = Math.Max(150, top.ClientSize.Width - 294); status.Width = Math.Max(170, top.ClientSize.Width - search.Width - level.Width - 24); };
        search.TextChanged += (_, _) => Fill(); level.SelectedIndexChanged += (_, _) => Fill();
        grid.SelectionChanged += (_, _) => ShowDetails();
        diagnosticTask = Load();
    }
}
