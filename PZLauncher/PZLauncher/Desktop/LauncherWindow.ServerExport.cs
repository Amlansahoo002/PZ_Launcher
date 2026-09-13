namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private static string ServerExportTitle => T("export.title");

    private Form BuildServerExportDialog(ServerPreset preset, bool passwordAvailable, out ComboBox formatInput, out TextBox cacheInput, out CheckBox passwordInput)
    {
        var dialog = new Form
        {
            Text = ServerExportTitle, StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(670, 475), MinimumSize = new Size(640, 510),
            BackColor = Theme.Background, ForeColor = Theme.Text, Font = Theme.Font(),
            FormBorderStyle = FormBorderStyle.Sizable, MaximizeBox = false, MinimizeBox = false,
            AutoScaleMode = AutoScaleMode.Dpi, Padding = new Padding(22)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        foreach (int height in new[] { 38, 32, 45, 28, 42, 42, 140, 44 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        dialog.Controls.Add(layout);
        var title = Theme.Label(ServerExportTitle + " · " + preset.Name, 16, true); title.Dock = DockStyle.Fill; layout.Controls.Add(title);
        var formatLabel = Theme.Label(T("export.format")); formatLabel.Dock = DockStyle.Fill; layout.Controls.Add(formatLabel);
        var format = new ComboBox { Dock = DockStyle.Top }; StyleCombo(format);
        format.Items.AddRange(["Windows (.bat)", "Linux (.sh)", "Windows + Linux (.bat + .sh)"]); format.SelectedIndex = 0;
        layout.Controls.Add(format);
        var cacheLabel = Theme.Label(T("export.linuxCache")); cacheLabel.Dock = DockStyle.Fill; layout.Controls.Add(cacheLabel);
        var cache = new TextBox { Text = "/home/pz/Zomboid", Dock = DockStyle.Top, BackColor = Theme.Surface, ForeColor = Theme.Text }; layout.Controls.Add(cache);
        var includePassword = Check(T("export.password"), false);
        includePassword.Enabled = passwordAvailable; includePassword.Dock = DockStyle.Fill; layout.Controls.Add(includePassword);
        var note = Theme.Label("", 9, color: Theme.Muted); note.Dock = DockStyle.Fill; layout.Controls.Add(note);
        void RefreshFormat()
        {
            cache.Enabled = cacheLabel.Enabled = format.SelectedIndex != 0;
            note.Text = T("export.help");
        }
        format.SelectedIndexChanged += (_, _) => RefreshFormat(); RefreshFormat();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft }; layout.Controls.Add(actions);
        var export = new ActionButton { Text = ServerExportTitle, Width = 215, Height = 36, DialogResult = DialogResult.OK };
        var cancel = new ActionButton { Text = T("dialog.cancel"), Width = 160, Height = 36, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(export); actions.Controls.Add(cancel); dialog.AcceptButton = export; dialog.CancelButton = cancel;
        formatInput = format; cacheInput = cache; passwordInput = includePassword;
        return dialog;
    }

    private async Task ExportServerScripts(ServerPreset preset, string password)
    {
        using var dialog = BuildServerExportDialog(preset, password.Length > 0, out var format, out var cache, out var includePassword);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var folder = new FolderBrowserDialog { Description = ServerExportTitle, UseDescriptionForTitle = true };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        bool windows = format.SelectedIndex != 1, linux = format.SelectedIndex != 0;
        string target = Path.Combine(folder.SelectedPath, preset.Name + "-launch-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..4]);
        string linuxCache = cache.Text.Trim(), secret = includePassword.Checked ? password : "";
        await WorldWork(pageHost, () => ServerLaunchScripts.Export(ServerFiles.Plan(state.Installation, preset, secret), target, preset.Name, windows, linux, linuxCache));
    }
}
