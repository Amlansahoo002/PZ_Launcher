namespace PZLauncher.Desktop;

internal sealed class ServerProbeDialog : Form
{
    private CancellationTokenSource? operation;
    private bool closePending;
    internal ServerModCapture? ModCapture { get; private set; }
    internal ServerProbeDialog(string installation, OnlineServerInfo server, CancellationToken lifetime, bool steam = true)
    {
        Name = "GrabServerModsDialog"; Text = T("probe.title"); Font = Theme.Font();
        BackColor = Theme.Background; ForeColor = Theme.Text; AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent; ClientSize = new(720, 590); MinimumSize = new(650, 625);
        FormBorderStyle = FormBorderStyle.Sizable; SizeGripStyle = SizeGripStyle.Show;
        ShowInTaskbar = false; MinimizeBox = false; Padding = new(22);
        var heading = Theme.Label(server.Name + "  ·  " + server.Host + ":" + server.Port, 13, true);
        heading.Dock = DockStyle.Top; heading.Height = 45;
        var note = Theme.Label(T("probe.note"), 9, color: Theme.Muted); note.Dock = DockStyle.Top; note.Height = 115;
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, Height = 220, ColumnCount = 2, RowCount = 5 };
        fields.ColumnStyles.Add(new(SizeType.Percent, 42)); fields.ColumnStyles.Add(new(SizeType.Percent, 58));
        TextBox Input(string key, bool secret, int row)
        {
            var label = Theme.Label(T(key), 9); label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft;
            var input = new TextBox { Name = key, Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = secret, MaxLength = 256, Margin = new(4, 8, 4, 4) };
            fields.RowStyles.Add(new(SizeType.Absolute, 42)); fields.Controls.Add(label, 0, row); fields.Controls.Add(input, 1, row);
            return input;
        }
        var user = Input("probe.user", false, 0); user.MaxLength = 64;
        var password = Input("probe.password", true, 1);
        var serverPassword = Input("online.password", true, 2);
        var code = Input("probe.code", true, 3); code.MaxLength = 8;
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var useSteam = new CheckBox { Text = T("online.steam"), AutoSize = true, Checked = steam && !InstallationLocator.IsGog(installation) };
        var relay = new CheckBox { Text = T("probe.relay"), AutoSize = true };
        options.Controls.AddRange([useSteam, relay]); fields.Controls.Add(options, 0, 4); fields.SetColumnSpan(options, 2);
        var status = Theme.Label(T("probe.enter"), 9, color: Theme.Muted); status.Name = "GrabModsStatus"; status.Dock = DockStyle.Fill;
        var progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 6, Style = ProgressBarStyle.Marquee, Visible = false };
        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 54 };
        var start = new ActionButton(T("probe.start")) { Name = "StartGrabMods", Width = 230, Height = 40, Dock = DockStyle.Left, Primary = true };
        var close = new ActionButton(T("dialog.close")) { Width = 145, Height = 40, Dock = DockStyle.Right };
        buttons.Controls.AddRange([start, close]);
        Controls.Add(status); Controls.Add(fields); Controls.Add(note); Controls.Add(heading); Controls.Add(progress); Controls.Add(buttons);
        start.Click += async (_, _) =>
        {
            if (operation != null) return;
            if (string.IsNullOrWhiteSpace(user.Text)) { status.Text = T("probe.userRequired"); user.Focus(); return; }
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime); operation = cancellation;
            fields.Enabled = start.Enabled = false; close.Text = T("dialog.cancel"); progress.Visible = true; status.Text = T("probe.starting");
            try
            {
                ModCapture = await ServerModProbe.RunAsync(installation, server,
                    new(user.Text, password.Text, serverPassword.Text, code.Text, useSteam.Checked, relay.Checked), cancellation.Token,
                    new Progress<string>(message => { if (!IsDisposed) status.Text = message; }));
                DialogResult = DialogResult.OK; closePending = true;
            }
            catch (OperationCanceledException) { status.Text = T("online.cancelled"); }
            catch (Exception ex) { status.Text = ex.Message; }
            finally
            {
                operation = null; fields.Enabled = start.Enabled = true; close.Text = T("dialog.close"); progress.Visible = false;
                password.Clear(); serverPassword.Clear(); code.Clear();
                if (closePending) Close();
            }
        };
        close.Click += (_, _) => { if (operation != null) operation.Cancel(); else Close(); };
        FormClosing += (_, e) => { if (operation != null) { closePending = true; e.Cancel = true; operation.Cancel(); } };
        AcceptButton = start; CancelButton = close;
    }
}
