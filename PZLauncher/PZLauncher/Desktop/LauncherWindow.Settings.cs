using System.Globalization;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private NumericUpDown? memoryEditor;
    private ComboBox? collectorEditor;
    private CheckBox? steamEditor;
    private CheckBox? voiceEditor;
    private CheckBox? debugEditor;
    private readonly List<Action<PlayerProfile>> preferenceReaders = [];

    private void BuildSettings()
    {
        optionReaders.Clear(); optionInitial.Clear();
        preferenceReaders.Clear();
        options = new OptionDocument(Path.Combine(profile.CachePath, "options.ini"));
        var panel = new Panel { Dock = DockStyle.Fill }; pageHost.Controls.Add(panel);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(0, 8, 0, 7) };
        var save = new ActionButton(T("settings.save")) { Primary = true, Width = 240, Dock = DockStyle.Right };
        save.Click += (_, _) => { if (SaveSettings()) footerDetail.Text = T("settings.saved"); }; bottom.Controls.Add(save);
        var hint = Theme.Label(T("settings.backup"), 9, color: Theme.Muted); hint.Dock = DockStyle.Fill; hint.TextAlign = ContentAlignment.MiddleLeft; bottom.Controls.Add(hint);
        bottom.Controls.SetChildIndex(hint, 0);
        var tabs = new ThemedTabs { Dock = DockStyle.Fill, Font = Theme.Font(10) };
        panel.Controls.Add(tabs); panel.Controls.Add(bottom);
        Control Tab(string title, bool flow = true)
        {
            var tab = new Panel { Text = title, BackColor = Theme.Background, ForeColor = Theme.Text, Padding = new Padding(8) };
            tabs.TabPages.Add(tab);
            if (!flow) return tab;
            var content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false,
                FlowDirection = FlowDirection.TopDown, Padding = new Padding(0, 4, 0, 12) };
            tab.Controls.Add(content);
            void ArrangeRows()
            {
                
                int width = Math.Max(300, content.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 14);
                foreach (Control child in content.Controls)
                {
                    child.Anchor = AnchorStyles.Left | AnchorStyles.Top;
                    child.Margin = new Padding(0, 0, 0, child is Label ? 6 : 4);
                    if (child is not ActionButton && child.Width != width) child.Width = width;
                }
            }
            content.SizeChanged += (_, _) => ArrangeRows();
            content.ControlAdded += (_, _) => ArrangeRows();
            return content;
        }
        BuildInstallationSettings(Tab(T("install.tab")));
        var installationTab = tabs.TabPages[0];
        bottom.Visible = false;
        var display = Tab(T("tab.display")); int y = 17;
        AddSection(display, T("section.display"), ref y);
        AddNumber(display, "width", T("option.width"), T("option.widthTip"), 640, 16384, ref y);
        AddNumber(display, "height", T("option.height"), T("option.heightTip"), 480, 16384, ref y);
        AddBoolean(display, "fullScreen", T("option.fullScreen"), T("option.fullScreenTip"), ref y);
        AddBoolean(display, "borderless", T("option.borderless"), T("option.borderlessTip"), ref y);
        AddBoolean(display, "vsync", T("option.vsync"), T("option.vsyncTip"), ref y);
        AddNumber(display, "frameRate", T("option.fps"), T("option.fpsTip"), 24, 244, ref y);
        AddBoolean(display, "texture2x", T("option.textures"), T("option.texturesTip"), ref y);
        AddNumber(display, "bloodDecals", T("option.blood"), T("option.bloodTip"), 0, 10, ref y);
        AddSection(display, T("section.sound"), ref y);
        AddNumber(display, "soundVolume", T("option.sound"), T("option.soundTip"), 0, 10, ref y);
        AddNumber(display, "musicVolume", T("option.music"), T("option.musicTip"), 0, 10, ref y);
        AddNumber(display, "ambientVolume", T("option.ambient"), T("option.ambientTip"), 0, 10, ref y);

        var comfort = Tab(T("tab.comfort")); y = 17;
        AddSection(comfort, T("section.comfort"), ref y);
        AddBoolean(comfort, "autoDrink", T("option.drink"), T("option.drinkTip"), ref y);
        AddBoolean(comfort, "panCameraWhileAiming", T("option.aim"), T("option.aimTip"), ref y);
        AddBoolean(comfort, "panCameraWhileDriving", T("option.drive"), T("option.driveTip"), ref y);
        AddBoolean(comfort, "showItemModInfo", T("option.modInfo"), T("option.modInfoTip"), ref y);
        AddBoolean(comfort, "clock24Hour", T("option.clock"), T("option.clockTip"), ref y);
        AddBoolean(comfort, "enableDyslexicFont", T("option.font"), T("option.fontTip"), ref y);
        AddBoolean(comfort, "colorblindPatterns", T("option.colorblind"), T("option.colorblindTip"), ref y);
        AddBoolean(comfort, "lockCursorToWindow", T("option.cursor"), T("option.cursorTip"), ref y);

        var runtime = Tab(T("tab.launch")); y = 17;
        AddSection(runtime, T("section.launch"), ref y);
        bool gog = InstallationLocator.IsGog(state.Installation);
        steamEditor = Check(T("steam.use"), profile.Steam && !gog); AddRuntimeRow(runtime, steamEditor, T(gog ? "install.gogMode" : "option.steamTip"), ref y);
        steamEditor.Enabled = profile.OnlineServerId.Length == 0 && !gog;
        voiceEditor = Check(T("option.voice"), profile.Voice); AddRuntimeRow(runtime, voiceEditor, T("option.voiceTip"), ref y);
        debugEditor = Check(T("option.debug"), profile.Debug); AddRuntimeRow(runtime, debugEditor, T("option.debugTip"), ref y);
        ProfileCheck(runtime, "launch.safe", profile.Launch.SafeMode, (p, v) => p.Launch.SafeMode = v, ref y);
        ProfileCheck(runtime, "launch.noSound", profile.Launch.NoSound, (p, v) => p.Launch.NoSound = v, ref y);
        ProfileCheck(runtime, "launch.imgui", profile.Launch.ImGui, (p, v) => { p.Launch.ImGui = v; if (v) p.Debug = true; }, ref y);
        ProfileCheck(runtime, "launch.viewports", profile.Launch.ImGuiViewports, (p, v) => { p.Launch.ImGuiViewports = v; if (v) p.Debug = true; }, ref y);
        ProfileCheck(runtime, "launch.translation", profile.Launch.DebugTranslation, (p, v) => p.Launch.DebugTranslation = v, ref y);
        ProfileCheck(runtime, "launch.ai", profile.Launch.AiTest, (p, v) => p.Launch.AiTest = v, ref y);
        ProfileCheck(runtime, "launch.antiCheats", profile.Launch.AntiCheats, (p, v) => p.Launch.AntiCheats = v, ref y);
        ProfileText(runtime, "launch.debugLog", profile.Launch.DebugLog, (p, v) => p.Launch.DebugLog = v, ref y);
        ProfileText(runtime, "launch.debugConfig", profile.Launch.DebugConfig, (p, v) => p.Launch.DebugConfig = v, ref y);
        bool onlineConnection = profile.OnlineServerId.Length > 0 || profile.DedicatedServerId.Length > 0;
        bool pinnedFolders = profile.OnlineServerId.Length > 0 || profile.RuntimePackSha256.Length > 0;
        ProfileText(runtime, "launch.folders", pinnedFolders ? "mods" : profile.Launch.ModFolders, (p, v) => p.Launch.ModFolders = v, ref y, readOnly: pinnedFolders);
        ProfileText(runtime, "launch.connect", profile.Launch.ConnectAddress, (p, v) => p.Launch.ConnectAddress = v, ref y, readOnly: onlineConnection);
        ProfileText(runtime, "launch.password", profile.Launch.ConnectPassword, (p, v) => p.Launch.ConnectPassword = v, ref y, true, onlineConnection);
        ProfileCheck(runtime, "launch.netLog", profile.NetworkLogging, (p, v) => p.NetworkLogging = v, ref y);
        ProfileNumber(runtime, "launch.logSize", profile.ConsoleLogSizeKb, 1024, 2097152, (p, v) => p.ConsoleLogSizeKb = v, ref y);

        var launchTab = runtime;
        runtime = Tab(T("tab.jvm")); y = 0;
        BuildHardwareAdvice(runtime);
        AddSection(runtime, T("section.java"), ref y);
        var automatic = ProfileCheck(runtime, "jvm.automatic", profile.AutomaticJvm, (p, v) => p.AutomaticJvm = v, ref y);
        ProfileNumber(runtime, "jvm.xms", profile.InitialMemoryMb, 256, 65536, (p, v) => p.InitialMemoryMb = v, ref y, out var initialMemory);
        memoryEditor = new NumericUpDown { Minimum = 1024, Maximum = 65536, Increment = 512, Value = Math.Clamp(profile.MemoryMb, 1024, 65536), BackColor = Theme.Surface, ForeColor = Theme.Text, ThousandsSeparator = true };
        AddEditorRow(runtime, T("option.memory"), T("option.memoryTip"), memoryEditor, ref y);
        collectorEditor = new ComboBox(); StyleCombo(collectorEditor); collectorEditor.Items.AddRange(["G1", "ZGC"]); collectorEditor.SelectedIndex = profile.Collector == "ZGC" ? 1 : 0;
        if (collectorEditor.SelectedIndex < 0) collectorEditor.SelectedIndex = 0;
        AddEditorRow(runtime, T("option.collector"), T("option.collectorTip"), collectorEditor, ref y);
        ProfileNumber(runtime, "jvm.stack", profile.StackKb, 256, 16384, (p, v) => p.StackKb = v, ref y, out var stack);
        ProfileNumber(runtime, "jvm.pause", profile.PauseTargetMs, 10, 2000, (p, v) => p.PauseTargetMs = v, ref y, out var pause);
        var dedup = ProfileCheck(runtime, "jvm.dedup", profile.StringDeduplication, (p, v) => p.StringDeduplication = v, ref y);
        ProfileCheck(runtime, "jvm.logging", profile.GcLogging, (p, v) => p.GcLogging = v, ref y);
        void CollectorChanged()
        {
            initialMemory.Enabled = memoryEditor.Enabled = stack.Enabled = collectorEditor.Enabled = !automatic.Checked;
            pause.Enabled = dedup.Enabled = !automatic.Checked && collectorEditor.SelectedIndex == 0;
        }
        collectorEditor.SelectedIndexChanged += (_, _) => CollectorChanged(); CollectorChanged();
        recommendationApply = suggestion =>
        {
            initialMemory.Value = suggestion.XmsMb; memoryEditor.Value = suggestion.XmxMb;
            stack.Value = suggestion.StackKb; pause.Value = suggestion.PauseTargetMs;
            collectorEditor.SelectedIndex = suggestion.Collector == "ZGC" ? 1 : 0; dedup.Checked = suggestion.StringDeduplication;
            settingsDirty = true;
        };
        automatic.CheckedChanged += (_, _) => { if (automatic.Checked) recommendationApply(SuggestedJvm); CollectorChanged(); };
        var arguments = new ActionButton(T("jvm.arguments")) { Width = 350, Height = 36 };
        arguments.Click += (_, _) => { if (SaveSettings()) EditJvmArguments(); };
        runtime.Controls.Add(arguments);
        runtime = launchTab;
        AddSection(runtime, T("section.install"), ref y);
        var folders = new FlowLayoutPanel { Height = 36, WrapContents = false };
        var cache = new ActionButton(T("profile.folder")) { Width = 220, Height = 34, Margin = Padding.Empty };
        cache.Click += (_, _) => OpenFolder(profile.CachePath); folders.Controls.Add(cache); runtime.Controls.Add(folders);
        var command = new ActionButton(T("launch.preview")) { Width = 305, Height = 34 };
        command.Click += (_, _) => ShowLaunchCommand(); runtime.Controls.Add(command);
        foreach (var control in new Control[] { steamEditor, voiceEditor, debugEditor, memoryEditor, collectorEditor })
        {
            if (control is CheckBox check) check.CheckedChanged += (_, _) => settingsDirty = true;
            else if (control is NumericUpDown numeric) numeric.ValueChanged += (_, _) => settingsDirty = true;
            else if (control is ComboBox combo) combo.SelectedIndexChanged += (_, _) => settingsDirty = true;
        }
        var advanced = Tab(T("tab.advanced"), false);
        var explanation = Theme.Label(T("settings.advanced"), 9, color: Theme.Muted);
        explanation.Dock = DockStyle.Top; explanation.Height = 42;
        var grid = Theme.Grid(); grid.RowTemplate.Height = 27; grid.ColumnHeadersHeight = 34;
        grid.Font = Theme.Font(9.5f);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "key", HeaderText = T("column.option"), ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "value", HeaderText = T("column.value"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (var entry in options.Values.OrderBy(e => e.Key))
        {
            if (optionReaders.ContainsKey(entry.Key)) continue;
            int row = grid.Rows.Add(entry.Key, entry.Value); string key = entry.Key;
            optionInitial[key] = entry.Value;
            optionReaders[key] = () => Convert.ToString(grid.Rows[row].Cells[1].Value) ?? "";
        }
        grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) settingsDirty = true; };
        advanced.Controls.Add(grid); advanced.Controls.Add(explanation);
        BuildSharing(Tab(T("tab.sharing")));
        var languages = Tab(T("language.packs"));
        var languagesTab = tabs.TabPages[^1];
        void UpdateSaveVisibility() => bottom.Visible = !installationTab.Visible && !languagesTab.Visible;
        installationTab.VisibleChanged += (_, _) => UpdateSaveVisibility();
        languagesTab.VisibleChanged += (_, _) => UpdateSaveVisibility();
        var languageHelp = Theme.Label(T("language.packsHelp"), 10, color: Theme.Muted);
        languageHelp.AutoSize = true;
        languageHelp.MaximumSize = new Size(700, 0);
        languages.Controls.Add(languageHelp);
        var openPacks = new ActionButton(T("language.openPacks")) { Width = 380, Height = 42 };
        openPacks.Click += (_, _) =>
        {
            try { LanguagePacks.WriteContributorFiles(L.PacksDirectory, L.Catalog("en")); OpenFolder(L.PacksDirectory); }
            catch (Exception ex) { ShowError(ex); }
        };
        languages.Controls.Add(openPacks);
        var packPath = Theme.Label(L.PacksDirectory, 9, color: Theme.Muted);
        packPath.Height = 48; languages.Controls.Add(packPath);
        tips.SetToolTip(packPath, L.PacksDirectory);
        if (L.PackIssues.Count > 0)
        {
            var packErrors = new ActionButton(T("language.issues", L.PackIssues.Count)) { Width = 380, Height = 42 };
            packErrors.Click += (_, _) => ShowLocalizedMessage(string.Join("\r\n\r\n", L.PackIssues), T("language.packs"), false);
            languages.Controls.Add(packErrors);
        }
        settingsDirty = false;
    }
    private void AddSection(Control parent, string text, ref int y)
    {
        var title = Theme.Label(text, 12, true); title.SetBounds(8, y, 780, 28); parent.Controls.Add(title); y += 34;
    }
    private void AddBoolean(Control parent, string key, string text, string description, ref int y)
    {
        if (options == null || !options.Values.TryGetValue(key, out var raw) || !bool.TryParse(raw, out var value)) return;
        var input = Check(T("option.enabled"), value);
        AddEditorRow(parent, text, description, input, ref y);
        optionInitial[key] = raw; optionReaders[key] = () => input.Checked ? "true" : "false";
        input.CheckedChanged += (_, _) => settingsDirty = true;
    }
    private void AddNumber(Control parent, string key, string text, string description, int min, int max, ref int y)
    {
        if (options == null || !options.Values.TryGetValue(key, out var raw) || !int.TryParse(raw, out var value)) return;
        var input = new NumericUpDown { Minimum = Math.Min(min, value), Maximum = Math.Max(max, value), Value = value, BackColor = Theme.Surface, ForeColor = Theme.Text };
        AddEditorRow(parent, text, description, input, ref y);
        optionInitial[key] = raw; optionReaders[key] = () => input.Value.ToString(CultureInfo.InvariantCulture);
        input.ValueChanged += (_, _) => settingsDirty = true;
    }
    private void AddEditorRow(Control parent, string title, string description, Control editor, ref int y)
    {
        var row = new SurfacePanel { Size = new Size(815, 42), Tag = "setting-row", Outline = false };
        var label = Theme.Label(title, 10, true); label.TextAlign = ContentAlignment.MiddleLeft;
        row.Controls.Add(label);
        var help = new ActionButton("?") { Font = Theme.Font(9, true), AccessibleName = title,
            AccessibleDescription = description, Size = new Size(26, 26) };
        help.Click += (_, _) => ShowLocalizedMessage(description, title, false);
        row.Controls.Add(help);
        editor.AccessibleName = title; editor.AccessibleDescription = description;
        foreach (var control in new Control[] { label, editor, help, row }) tips.SetToolTip(control, description);
        row.Controls.Add(editor);
        void ArrangeRow()
        {
            const int editorWidth = 168;
            int textWidth = Math.Max(130, row.Width - editorWidth - 76);
            int titleHeight = TextRenderer.MeasureText(label.Text, label.Font, new Size(textWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            int height = Math.Max(42, titleHeight + 16);
            if (row.Height != height) row.Height = height;
            label.SetBounds(12, 5, textWidth, height - 10);
            editor.SetBounds(row.Width - editorWidth - 12, (row.Height - 30) / 2, editorWidth, 30);
            help.SetBounds(editor.Left - 36, (row.Height - 26) / 2, 26, 26);
        }
        row.SizeChanged += (_, _) => ArrangeRow();
        parent.Controls.Add(row); ArrangeRow(); y += row.Height + 4;
    }
    private void AddRuntimeRow(Control parent, CheckBox input, string text, ref int y)
    {
        var title = input.Text; input.Text = T("option.enabled"); AddEditorRow(parent, title, text, input, ref y);
    }
    private bool SaveSettings()
    {
        if (!settingsDirty || options == null) return true;
        try
        {
            Validate();
            var edits = optionReaders.Select(pair => (key: pair.Key, value: pair.Value()))
                .Where(e => e.value != optionInitial[e.key]).ToDictionary(e => e.key, e => e.value);
            var updated = System.Text.Json.JsonSerializer.Deserialize<PlayerProfile>(System.Text.Json.JsonSerializer.Serialize(profile))!;
            if (steamEditor != null && !InstallationLocator.IsGog(state.Installation)) updated.Steam = steamEditor.Checked;
            if (voiceEditor != null) updated.Voice = voiceEditor.Checked;
            if (debugEditor != null) updated.Debug = debugEditor.Checked;
            if (memoryEditor != null) updated.MemoryMb = (int)memoryEditor.Value;
            if (collectorEditor != null) updated.Collector = collectorEditor.SelectedIndex == 1 ? "ZGC" : "G1";
            foreach (var read in preferenceReaders) read(updated);
            if (updated.AutomaticJvm) JvmArguments.ApplySuggestion(updated, SuggestedJvm);
            ProfileSharing.ValidatePreferences(updated);
            if (InstallationLocator.IsValid(state.Installation))
                _ = launchService.CreateLaunchPlan(state.Installation, updated.AsGameProfile(), updated.AsJvmProfile());
            if (edits.Count > 0) options.SaveChanges(edits);
            foreach (var (key, value) in edits) optionInitial[key] = value;
            int index = state.Profiles.FindIndex(p => p.Id == profile.Id);
            state.Profiles[index] = updated; profile = updated; ReloadProfileCombo();
            LauncherStorage.Save(state); settingsDirty = false; UpdateFooter(); _ = RefreshLibraryAsync(); return true;
        }
        catch (Exception ex) { ShowError(ex); return false; }
    }
    private void ShowLaunchCommand()
    {
        try
        {
            if (!ResolveDrafts()) return;
            if (profile.DedicatedServerId.Length > 0) RuntimeClients.SetConnection(profile, RuntimeClients.VerifyBinding(state, profile, state.Installation));
            var plan = launchService.CreateLaunchPlan(state.Installation, profile.AsGameProfile(), profile.AsJvmProfile());
            plan = RuntimeClients.Attach(plan, profile, state.Installation);
            if (profile.JavaLoaderEnabled)
            {
                var java = JavaMods.Prepare(profile, JavaMods.Scan(profile, mods, selection.Ids, state.Installation), state.Installation);
                RuntimeClients.CheckOverlayConflicts(plan.Arguments[plan.Arguments.ToList().IndexOf("-cp") + 1].Split(';').Where(Path.IsPathRooted), java);
                plan = JavaMods.Attach(plan, java);
            }
            using var dialog = new Form { Text = T("launch.commandTitle") + profile.Name, Size = new Size(840, 360), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Background };
            dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Surface,
                ForeColor = Theme.Text, Font = new Font("Consolas", 10), Text = plan.CommandLinePreview });
            dialog.ShowDialog(this);
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
