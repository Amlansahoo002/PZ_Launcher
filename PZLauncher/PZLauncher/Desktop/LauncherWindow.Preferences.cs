using System.Text.Json;

namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private Action<JvmSuggestion>? recommendationApply;
    private HardwareSnapshot? hardware;
    private Task<HardwareSnapshot>? hardwareDetection;
    private JvmSuggestion SuggestedJvm => HardwareAdvisor.Suggest(hardware ?? new(0, 0, Environment.ProcessorCount, "", "", ""));
    private Task<HardwareSnapshot> DetectMachineAsync()
    {
        string installation = state.Installation;
        return Task.Run(() =>
        {
            try { return HardwareAdvisor.Detect(installation); }
            catch (Exception ex) { LauncherStorage.Log("Hardware detection: " + ex.Message); return new HardwareSnapshot(0, 0, Environment.ProcessorCount, "", "", ""); }
        });
    }
    private async Task InitializeHardwareAsync()
    {
        hardware = await (hardwareDetection ??= DetectMachineAsync());
        if (IsDisposed) return;
        bool changed = false;
        foreach (var item in state.Profiles) changed |= HardwareAdvisor.FillUnset(item, SuggestedJvm);
        if (changed && !rendering) LauncherStorage.Save(state);
        LauncherStorage.Log("Hardware: " + JsonSerializer.Serialize(hardware) + " / JVM suggestion: " + JsonSerializer.Serialize(SuggestedJvm));
    }
    private CheckBox ProfileCheck(Control parent, string key, bool value, Action<PlayerProfile, bool> write, ref int y)
    {
        var check = Check(T("option.enabled"), value);
        AddEditorRow(parent, T(key), T(key + "Tip"), check, ref y);
        preferenceReaders.Add(p => write(p, check.Checked));
        check.CheckedChanged += (_, _) => settingsDirty = true;
        return check;
    }
    private void ProfileText(Control parent, string key, string value, Action<PlayerProfile, string> write, ref int y, bool secret = false, bool readOnly = false)
    {
        var input = new TextBox { Text = value, BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = secret, MaxLength = 2048 };
        input.ReadOnly = readOnly;
        AddEditorRow(parent, T(key), T(readOnly ? "online.editConnection" : key + "Tip"), input, ref y);
        preferenceReaders.Add(p => write(p, input.Text.Trim()));
        input.TextChanged += (_, _) => settingsDirty = true;
    }
    private void ProfileNumber(Control parent, string key, int value, int min, int max, Action<PlayerProfile, int> write, ref int y) =>
        ProfileNumber(parent, key, value, min, max, write, ref y, out _);
    private void ProfileNumber(Control parent, string key, int value, int min, int max, Action<PlayerProfile, int> write, ref int y, out NumericUpDown input)
    {
        var number = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), ThousandsSeparator = true, BackColor = Theme.Surface, ForeColor = Theme.Text };
        input = number; AddEditorRow(parent, T(key), T(key + "Tip"), input, ref y);
        preferenceReaders.Add(p => write(p, (int)number.Value)); number.ValueChanged += (_, _) => settingsDirty = true;
    }
    private void BuildHardwareAdvice(Control parent)
    {
        recommendationApply = null;
        var card = new SurfacePanel();
        var title = Theme.Label(T("jvm.hardware"), 11, true); card.Controls.Add(title);
        var details = Theme.Label("", 9, color: Theme.Muted); card.Controls.Add(details);
        var detect = new ActionButton(T("jvm.detect")) { Width = 150, Height = 36, Font = Theme.Font(9, true) }; card.Controls.Add(detect);
        var apply = new ActionButton(T("jvm.apply")) { Width = 325, Height = 36, Font = Theme.Font(9, true), Primary = true }; card.Controls.Add(apply);
        var help = new ActionButton("?") { Width = 28, Height = 28 }; card.Controls.Add(help);
        help.Click += (_, _) => ShowLocalizedMessage(T("jvm.adviceTip"), T("jvm.hardware"), false);
        void Arrange()
        {
            int width = Math.Max(250, card.Width - 24);
            title.SetBounds(12, 7, width - 36, 24); help.Location = new Point(card.Width - 40, 5);
            int height = TextRenderer.MeasureText(details.Text, details.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            details.SetBounds(12, 34, width, Math.Max(18, height));
            detect.Location = new Point(12, details.Bottom + 8); apply.Location = new Point(170, detect.Top);
            card.Height = detect.Bottom + 9;
        }
        void Refresh()
        {
            var machine = hardware; var suggestion = SuggestedJvm;
            string gpu = machine == null || machine.Gpus.Count == 0 ? T("jvm.unknown") : string.Join(" + ", machine.Gpus.Select(g => T("jvm.gpu", g.Name, g.DedicatedMb / 1024.0)));
            details.Text = machine == null || machine.TotalMb == 0 ? T("jvm.detectFailed") : T("jvm.detected",
                machine.TotalMb / 1024.0, machine.AvailableMb / 1024.0, machine.Cpu, machine.Cores > 0 ? machine.Cores.ToString() : "?", machine.Threads, gpu,
                machine.JavaVersion, suggestion.Collector, suggestion.XmsMb, suggestion.XmxMb, suggestion.StackKb,
                suggestion.Collector == "G1" ? suggestion.PauseTargetMs + " ms" : T("jvm.notApplicable"));
            tips.SetToolTip(details, T("jvm.adviceTip") + "\n" + T("jvm.reserve", suggestion.ReservedMb)); Arrange();
        }
        card.SizeChanged += (_, _) => Arrange();
        detect.Click += async (_, _) =>
        {
            detect.Enabled = apply.Enabled = false;
            hardwareDetection = DetectMachineAsync(); hardware = await hardwareDetection;
            if (!card.IsDisposed) { detect.Enabled = apply.Enabled = true; Refresh(); }
        };
        apply.Click += (_, _) => recommendationApply?.Invoke(SuggestedJvm);
        parent.Controls.Add(card); Refresh();
    }
    private void BuildSharing(Control parent)
    {
        var title = Theme.Label(T("sharing.title"), 14, true); title.Height = 34; parent.Controls.Add(title);
        var help = Theme.Label(T("sharing.help"), 10, color: Theme.Muted); help.Height = 90; parent.Controls.Add(help);
        var export = new ActionButton(T("sharing.export")) { Width = 290, Height = 40 };
        export.Click += (_, _) => ExportConfiguration(); parent.Controls.Add(export);
        var import = new ActionButton(T("sharing.import")) { Width = 290, Height = 40, Primary = true };
        import.Click += (_, _) => ImportConfiguration(); parent.Controls.Add(import);
    }
    private void ExportConfiguration()
    {
        if (!ResolveDrafts()) return;
        try
        {
            using var dialog = new SaveFileDialog { Filter = "PZLauncher (*.pzconfig.json)|*.pzconfig.json", FileName = "pz-profile.pzconfig.json", Title = T("sharing.export") };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var config = ProfileSharing.Export(profile, InstallationLocator.ReadGameVersion(state.Installation)?.ToString() ?? "", selection.Ids);
            ProfileSharing.Validate(config);
            LauncherStorage.WriteAtomic(dialog.FileName, JsonSerializer.Serialize(config, ProfileSharing.JsonOptions));
            footerDetail.Text = T("sharing.exported");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void ImportConfiguration()
    {
        if (!ResolveDrafts()) return;
        try
        {
            using var file = new OpenFileDialog { Filter = "PZLauncher (*.pzconfig.json;*.json)|*.pzconfig.json;*.json", Title = T("sharing.import") };
            if (file.ShowDialog(this) != DialogResult.OK) return;
            var config = ProfileSharing.Read(file.FileName);
            
            var incoming = JsonSerializer.Deserialize<PlayerProfile>(JsonSerializer.Serialize(config.Preferences))!;
            incoming.CachePath = Path.Combine(LauncherStorage.Root, "profiles", incoming.Id, "cache");
            var dependencies = ModResolver.Resolve(ModCatalog.Scan(incoming, InstallationLocator.ReadGameVersion(state.Installation)), config.Mods);
            using var preview = CreateMessageDialog(T("sharing.preview", config.Name, config.GameOptions.Count, config.Mods.Count, config.GameVersion) +
                (dependencies.Issues.Count > 0 ? "\r\n\r\n" + string.Join("\r\n", dependencies.Issues.Take(12)) : ""), T("sharing.import"), false);
            var accept = Descendants(preview).OfType<ActionButton>().Single();
            accept.Text = T("sharing.import"); accept.Width = 280;
            var cancel = new ActionButton(T("dialog.cancel")) { DialogResult = DialogResult.Cancel, Width = 160, Height = 40 };
            accept.Parent!.Controls.Add(cancel); preview.CancelButton = cancel;
            if (preview.ShowDialog(this) != DialogResult.OK) return;
            var next = ProfileSharing.Import(config, Path.Combine(LauncherStorage.Root, "profiles"));
            HardwareAdvisor.FillUnset(next, SuggestedJvm);
            state.Profiles.Add(next); profile = next; state.SelectedProfile = next.Id;
            LauncherStorage.Save(state); selection = new(profile.CachePath);
            ReloadProfileCombo(); ShowPage("Réglages"); UpdateFooter(); await RefreshLibraryAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
