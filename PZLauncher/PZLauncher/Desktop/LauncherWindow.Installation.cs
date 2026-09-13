namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private IReadOnlyList<GameInstallation> installations = [];
    private string? installationPathDraft;
    private bool installationBusy;
    private int installationScanGeneration;

    private void BuildInstallationSettings(Control parent)
    {
        var title = Theme.Label(T("install.title"), 14, true); title.Height = 28; parent.Controls.Add(title);
        var help = Theme.Label(T("install.help"), 9, color: Theme.Muted); help.Height = 36; parent.Controls.Add(help);
        var active = new TextBox { Name = "ActiveInstallation", Text = state.Installation, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Surface, ForeColor = Theme.Text, AccessibleName = T("install.current") };
        var currentLabel = Theme.Label(T("install.current"), 10, true); currentLabel.Height = 24; parent.Controls.Add(currentLabel); parent.Controls.Add(active);
        var status = Theme.Label(InstallationLocator.IsValid(state.Installation)
            ? T(InstallationLocator.IsGog(state.Installation) ? "install.gogMode" : "install.available") : T("install.unavailable"), 9, color: Theme.Muted);
        status.Height = 32; parent.Controls.Add(status);
        var detectedLabel = Theme.Label(T("install.detected"), 10, true); detectedLabel.Height = 24; parent.Controls.Add(detectedLabel);
        var detected = new ComboBox { Name = "DetectedInstallations", AccessibleName = T("install.detected"), DropDownWidth = 900 };
        StyleCombo(detected); parent.Controls.Add(detected);
        var found = Theme.Label("", 9, color: Theme.Muted); found.Height = 26; parent.Controls.Add(found);
        var actions = new FlowLayoutPanel { Height = 40, WrapContents = false };
        var rescan = new ActionButton(T("install.detect")) { Width = 200, Height = 36, Margin = new Padding(0, 0, 8, 0) };
        var select = new ActionButton(T("install.select")) { Width = 265, Height = 36, Margin = Padding.Empty };
        actions.Controls.Add(rescan); actions.Controls.Add(select); parent.Controls.Add(actions);
        void Fill()
        {
            detected.Items.Clear(); detected.Items.AddRange(installations.Cast<object>().ToArray());
            detected.SelectedItem = installations.FirstOrDefault(i => string.Equals(i.Path, state.Installation, StringComparison.OrdinalIgnoreCase));
            if (detected.SelectedIndex < 0 && detected.Items.Count > 0) detected.SelectedIndex = 0;
            select.Enabled = detected.Items.Count > 0; found.Text = T("install.found", installations.Count);
        }
        detected.SelectedIndexChanged += (_, _) => tips.SetToolTip(detected, (detected.SelectedItem as GameInstallation)?.Path ?? "");
        select.Click += async (_, _) => { if (detected.SelectedItem is GameInstallation choice) await ApplyInstallationAsync(choice.Path); };
        rescan.Click += async (_, _) =>
        {
            rescan.Enabled = false; found.Text = T("install.detecting");
            int generation = ++installationScanGeneration;
            string current = state.Installation; var remembered = state.InstallationPaths.ToArray();
            try
            {
                var result = await Task.Run(() => InstallationLocator.Discover(current, remembered));
                if (generation != installationScanGeneration) return;
                installations = result;
                if (!parent.IsDisposed) Fill();
            }
            catch (Exception ex) { if (!parent.IsDisposed) ShowError(ex); }
            finally { if (!rescan.IsDisposed) rescan.Enabled = true; }
        };
        Fill();
        var manualLabel = Theme.Label(T("install.path"), 10, true); manualLabel.Height = 26; parent.Controls.Add(manualLabel);
        var input = new TextBox { Name = "InstallationPath", Text = installationPathDraft ?? state.Installation, PlaceholderText = T("install.path"),
            BackColor = Theme.Surface, ForeColor = Theme.Text, AccessibleName = T("install.path") };
        input.TextChanged += (_, _) => installationPathDraft = input.Text;
        parent.Controls.Add(input);
        var manualActions = new FlowLayoutPanel { Height = 40, WrapContents = false };
        var browse = new ActionButton(T("install.browse")) { Width = 245, Height = 36, Margin = new Padding(0, 0, 8, 0) };
        var apply = new ActionButton(T("install.apply")) { Name = "ApplyInstallation", Width = 230, Height = 36, Primary = true, Margin = Padding.Empty };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = T("install.pick"), UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(input.Text) ? input.Text : state.Installation };
            if (dialog.ShowDialog(this) == DialogResult.OK) input.Text = dialog.SelectedPath;
        };
        apply.Click += async (_, _) => await ApplyInstallationAsync(input.Text);
        manualActions.Controls.Add(browse); manualActions.Controls.Add(apply); parent.Controls.Add(manualActions);
        var scope = Theme.Label(T("install.scope"), 9, color: Theme.Muted); scope.Height = 62; parent.Controls.Add(scope);
        parent.Controls.SetChildIndex(manualLabel, 5);
        parent.Controls.SetChildIndex(input, 6);
        parent.Controls.SetChildIndex(manualActions, 7);
    }
    private void PickInstallation()
    {
        if (!ResolveDrafts()) return;
        ShowPage("Réglages");
    }
    private async Task ApplyInstallationAsync(string path)
    {
        if (installationBusy || !ResolveDrafts()) return;
        if (game is { HasExited: false } || serverSession is { Running: true } || preparingLaunch || onlineBusy || workshopBusy || worldBusy)
        { ShowError(new InvalidOperationException(T("install.busy"))); return; }
        try
        {
            InstallationLocator.Remember(state, path);
            installationScanGeneration++;
            installationPathDraft = null;
            installationBusy = true; pageHost.Enabled = profileCombo.Enabled = languageCombo.Enabled = false;
            foreach (var button in navigation.Values) button.Enabled = false;
            libraryReady = false; UpdateFooter();
            if (!rendering) LauncherStorage.Save(state);
            installations = await Task.Run(() => InstallationLocator.Discover(state.Installation, state.InstallationPaths));
            hardwareDetection = DetectMachineAsync(); await InitializeHardwareAsync();
            if (IsDisposed) return;
            ShowPage(page); await RefreshLibraryAsync();
        }
        catch (Exception ex) { if (rendering) throw; if (!IsDisposed) ShowError(ex); }
        finally
        {
            installationBusy = false;
            if (!IsDisposed)
            {
                pageHost.Enabled = profileCombo.Enabled = languageCombo.Enabled = true;
                foreach (var button in navigation.Values) button.Enabled = true;
                UpdateFooter();
            }
        }
    }
}
