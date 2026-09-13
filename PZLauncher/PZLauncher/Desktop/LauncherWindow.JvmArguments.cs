namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private void EditJvmArguments()
    {
        using var dialog = BuildJvmArgumentsDialog(out var basis, out var extra, out var loose);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        profile.JvmArgumentBase = JvmArguments.Bases[basis.SelectedIndex];
        profile.ExtraJvmArguments = JvmArguments.Parse(extra.Text);
        profile.DirectLooseClassesFirst = loose.Checked;
        LauncherStorage.Save(state); ShowPage(page);
    }

    private Form BuildJvmArgumentsDialog(out ComboBox basis, out TextBox extra, out CheckBox loose)
    {
        var dialog = new Form { Text = T("jvm.arguments"), ClientSize = new Size(830, 590), MinimumSize = new Size(750, 570),
            StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Background, ForeColor = Theme.Text, Font = Theme.Font() };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
        panel.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (float height in new[] { 32f, 70f, 0f, 52f, 65f, 42f }) panel.RowStyles.Add(new(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height));
        dialog.Controls.Add(panel);
        var choice = new ComboBox { Dock = DockStyle.Fill }; StyleCombo(choice);
        choice.Items.AddRange(JvmArguments.Bases.Select(b => T("jvm.base." + b)).ToArray());
        choice.SelectedIndex = Math.Max(0, Array.IndexOf(JvmArguments.Bases, profile.JvmArgumentBase));
        panel.Controls.Add(choice);
        var help = Theme.Label(T("jvm.argumentsHelp"), 9, color: Theme.Muted); help.Dock = DockStyle.Fill; panel.Controls.Add(help);
        var text = new TextBox { Multiline = true, AcceptsReturn = true, WordWrap = false, ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = new Font("Consolas", 10),
            Text = string.Join(Environment.NewLine, profile.ExtraJvmArguments), MaxLength = 128 * 4096 };
        panel.Controls.Add(text);
        var priority = Check(T("jvm.looseFirst"), profile.DirectLooseClassesFirst); priority.Dock = DockStyle.Fill; panel.Controls.Add(priority);
        var error = Theme.Label("", 9, color: Theme.Accent); error.Dock = DockStyle.Fill; panel.Controls.Add(error);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var save = new ActionButton(T("settings.save")) { Width = 240, Height = 36, Primary = true, Margin = new Padding(3, 0, 3, 0) };
        save.Click += (_, _) =>
        {
            try { JvmArguments.Validate(JvmArguments.Bases[choice.SelectedIndex], JvmArguments.Parse(text.Text)); dialog.DialogResult = DialogResult.OK; }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        var cancel = new ActionButton(T("dialog.cancel")) { Width = 160, Height = 36, DialogResult = DialogResult.Cancel, Margin = new Padding(3, 0, 3, 0) };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); panel.Controls.Add(buttons); dialog.CancelButton = cancel;
        basis = choice; extra = text; loose = priority;
        return dialog;
    }
}
