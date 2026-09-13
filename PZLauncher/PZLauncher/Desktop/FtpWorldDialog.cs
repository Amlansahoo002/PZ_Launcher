using FluentFTP;

namespace PZLauncher.Desktop;

internal static class FtpText
{ internal static string Get(string key, params object[] args) => T("ftp." + key, args); }

internal sealed class FtpWorldDialog : Form
{
    internal FtpWorldWorkspace? Workspace { get; private set; }
    private CancellationTokenSource? cancellation;
    private bool busy;
    internal FtpWorldDialog()
    {
        Text = FtpText.Get("title"); Name = "FtpWorldDialog";
        BackColor = Theme.Background; ForeColor = Theme.Text; Font = Theme.Font();
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(800, 700); MinimumSize = new Size(720, 640); Padding = new Padding(16);
        MinimizeBox = false; ShowInTaskbar = false;
        var connection = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
        connection.ColumnStyles.Add(new(SizeType.Absolute, 105)); connection.ColumnStyles.Add(new(SizeType.Percent, 65));
        connection.ColumnStyles.Add(new(SizeType.Absolute, 100)); connection.ColumnStyles.Add(new(SizeType.Percent, 35));
        TextBox Field(string name, string value, int row, int col)
        {
            var label = Theme.Label(FtpText.Get(name), 9); label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft;
            var field = new TextBox { Text = value, Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text, Margin = new Padding(3, 7, 3, 7), AccessibleName = label.Text };
            connection.Controls.Add(label, col, row); connection.Controls.Add(field, col + 1, row); return field;
        }
        var host = Field("host", "", 0, 0); var port = Field("port", "21", 0, 2);
        var user = Field("user", "", 1, 0); var password = Field("password", "", 1, 2); password.UseSystemPasswordChar = true;
        var path = Field("directory", "/", 2, 0); connection.SetColumnSpan(path, 3);
        var protocolLabel = Theme.Label(FtpText.Get("protocol"), 9); protocolLabel.Dock = DockStyle.Fill;
        var protocol = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text };
        protocol.Items.AddRange(["FTP", "FTPS · TLS", "FTPS · TLS implicit"]); protocol.SelectedIndex = 0;
        protocol.SelectedIndexChanged += (_, _) => { if (port.Text is "21" or "990") port.Text = protocol.SelectedIndex == 2 ? "990" : "21"; };
        connection.Controls.Add(protocolLabel, 0, 3); connection.Controls.Add(protocol, 1, 3); connection.SetColumnSpan(protocol, 3);
        var header = new Panel { Dock = DockStyle.Top, Height = 195 }; header.Controls.Add(connection);
        var browseButtons = new ResponsiveToolbar { Dock = DockStyle.Bottom };
        var browse = new ActionButton(FtpText.Get("browse")) { Width = 240, Height = 40 };
        var up = new ActionButton(FtpText.Get("up")) { Width = 160, Height = 40 }; browseButtons.Controls.AddRange([browse, up]); header.Controls.Add(browseButtons);
        var resume = new ActionButton(FtpText.Get("resume")) { Width = 220, Height = 40 }; browseButtons.Controls.Add(resume);
        string? resumeManifest = null;
        var listing = Theme.Grid(); listing.ReadOnly = true; listing.Name = "FtpDirectoryListing";
        listing.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = FtpText.Get("directory"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        listing.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = FtpText.Get("size"), Width = 110 });
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 210, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var chunks = new StyledCheckBox { Text = FtpText.Get("includeChunks"), Checked = true, Height = 30, Width = 740 };
        var stopped = new StyledCheckBox { Text = FtpText.Get("stopped"), Height = 30, Width = 740 };
        var note = Theme.Label(FtpText.Get("help"), 9, color: Theme.Muted); note.Height = 54; note.Width = 740;
        var status = Theme.Label("", 9, color: Theme.Muted); status.Height = 32; status.Width = 740; status.AutoEllipsis = true;
        var actions = new FlowLayoutPanel { Height = 42, Width = 740, WrapContents = false };
        var download = new ActionButton(FtpText.Get("download")) { Width = 280, Height = 38, Primary = true };
        var cancel = new ActionButton(T("dialog.cancel")) { Width = 140, Height = 38 };
        actions.Controls.AddRange([download, cancel]); bottom.Controls.AddRange([chunks, stopped, note, status, actions]);
        Controls.Add(listing); Controls.Add(header); Controls.Add(bottom);
        bottom.SizeChanged += (_, _) => { foreach (Control control in bottom.Controls) control.Width = bottom.ClientSize.Width - 8; };
        FtpWorldSettings Settings()
        {
            if (!int.TryParse(port.Text, out int number) || number < 1 || number > 65535) throw new IOException(FtpText.Get("portInvalid"));
            return new() { Host = host.Text, Port = number, User = user.Text, Password = password.Text, Directory = FtpWorldConnection.RemoteDirectory(path.Text),
                Encryption = protocol.SelectedIndex switch { 1 => FtpEncryptionMode.Explicit, 2 => FtpEncryptionMode.Implicit, _ => FtpEncryptionMode.None } };
        }
        async Task Work(Func<CancellationToken, Task> action)
        {
            if (busy) return; busy = true; cancellation = new();
            connection.Enabled = browse.Enabled = up.Enabled = resume.Enabled = listing.Enabled = chunks.Enabled = stopped.Enabled = download.Enabled = false;
            try { await action(cancellation.Token); }
            catch (OperationCanceledException) { status.Text = FtpText.Get("cancelled"); }
            catch (Exception ex) { status.Text = ex.Message; MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally
            {
                busy = false; cancellation.Dispose(); cancellation = null;
                if (!IsDisposed)
                {
                    connection.Enabled = browse.Enabled = up.Enabled = resume.Enabled = listing.Enabled = stopped.Enabled = download.Enabled = true;
                    chunks.Enabled = resumeManifest == null;
                }
            }
        }
        async Task Browse(CancellationToken token)
        {
            var settings = Settings(); status.Text = FtpText.Get("listing", settings.Directory);
            using var remote = new FtpWorldConnection(settings);
            var entries = await remote.List("", token); listing.Rows.Clear();
            foreach (var entry in entries.OrderByDescending(e => e.Directory).ThenBy(e => e.Name, StringComparer.Ordinal))
            {
                int row = listing.Rows.Add((entry.Directory ? "▸ " : "") + entry.Name, entry.Directory ? "" : entry.Size.ToString("N0"));
                listing.Rows[row].Tag = entry;
            }
            path.Text = settings.Directory; status.Text = FtpText.Get("chooseDirectory");
        }
        browse.Click += async (_, _) => await Work(Browse);
        resume.Click += (_, _) =>
        {
            using var file = new OpenFileDialog { Filter = "FTP manifest (manifest.json)|manifest.json", InitialDirectory = Path.Combine(LauncherStorage.Root, "ftp-workspaces") };
            if (file.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var saved = FtpWorldWorkspace.ReadSettings(file.FileName);
                var savedCopy = FtpWorldWorkspace.Resume(file.FileName, saved);
                host.Text = saved.Host; port.Text = saved.Port.ToString(); user.Text = saved.User; path.Text = saved.Directory; password.Clear();
                chunks.Checked = savedCopy.IncludesChunks; chunks.Enabled = false;
                protocol.SelectedIndex = saved.Encryption switch { FtpEncryptionMode.Explicit => 1, FtpEncryptionMode.Implicit => 2, _ => 0 };
                port.Text = saved.Port.ToString(); resumeManifest = file.FileName; download.Text = FtpText.Get("resumeOpen"); status.Text = FtpText.Get("resumeHelp");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        up.Click += async (_, _) => await Work(async token =>
        { string current = FtpWorldConnection.RemoteDirectory(path.Text); path.Text = current[..Math.Max(0, current.LastIndexOf('/'))]; if (path.Text.Length == 0) path.Text = "/"; await Browse(token); });
        listing.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || listing.Rows[e.RowIndex].Tag is not RemoteWorldEntry { Directory: true } entry) return;
            await Work(async token => { path.Text = FtpWorldConnection.RemoteDirectory(path.Text).TrimEnd('/') + "/" + entry.Name; await Browse(token); });
        };
        download.Click += async (_, _) =>
        {
            if (!stopped.Checked) { MessageBox.Show(this, FtpText.Get("stopRequired"), Text); return; }
            bool complete = chunks.Checked;
            await Work(async token =>
            {
                var settings = Settings();
                if (resumeManifest != null)
                {
                    using var remote = new FtpWorldConnection(settings); await remote.List("", token);
                    Workspace = FtpWorldWorkspace.Resume(resumeManifest, settings); return;
                }
                string local = Path.Combine(LauncherStorage.Root, "ftp-workspaces", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
                var workspace = new FtpWorldWorkspace(local, settings.Host + ":" + settings.Port + settings.Directory, complete, () => new FtpWorldConnection(settings), settings);
                await Task.Run(() => workspace.Download(new Progress<string>(text => { if (!IsDisposed) BeginInvoke(() => status.Text = text); }), token), token);
                Workspace = workspace;
            });
            if (Workspace != null) { password.Clear(); DialogResult = DialogResult.OK; Close(); }
        };
        cancel.Click += (_, _) => { if (busy) cancellation?.Cancel(); else Close(); };
        FormClosing += (_, e) => { if (busy) { cancellation?.Cancel(); e.Cancel = true; } };
    }
}
