using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private ServerSession? serverSession;
    private ServerPreset? serverPreset;
    private bool serverDirty;
    private Func<bool>? saveServerDraft;
    private Task configurationTask = Task.CompletedTask;
    private void BuildServers()
    {
        foreach (var cache in state.Profiles.Select(p => p.CachePath).Append(LauncherStorage.DefaultGameData).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string directory = Path.Combine(cache, "Server");
            if (!Directory.Exists(directory)) continue;
            foreach (string file in Directory.EnumerateFiles(directory, "*.ini"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.EndsWith("_zombies") || !Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,64}$")) continue;
                if (!state.Servers.Any(s => s.Name == name && s.CachePath.Equals(cache, StringComparison.OrdinalIgnoreCase)))
                {
                    var detected = new ServerPreset { Name = name, CachePath = cache };
                    HardwareAdvisor.ConfigureServer(detected, SuggestedJvm, true); state.Servers.Add(detected);
                }
            }
        }
        serverPreset ??= state.Servers.FirstOrDefault();
        var panel = new Panel { Dock = DockStyle.Fill }; pageHost.Controls.Add(panel);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        var choices = new ComboBox { Width = 280 }; StyleCombo(choices); choices.Items.AddRange(state.Servers.ToArray()); choices.SelectedItem = serverPreset; bar.Controls.Add(choices);
        ToolbarButton(bar, T("server.new"), 170, () =>
        {
            if (!ResolveDrafts()) return;
            using var dialog = BuildProfileDialog(out var name);
            dialog.Text = T("server.new"); name.PlaceholderText = "myserver";
            foreach (var label in Descendants(dialog).OfType<Label>()) label.Text = label.Font.Size > 12 ? T("server.new") : T("server.nameHelp");
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var preset = new ServerPreset { Name = name.Text.Trim(), CachePath = Path.Combine(LauncherStorage.Root, "server-caches", Guid.NewGuid().ToString("N")), Steam = profile.Steam };
                HardwareAdvisor.ConfigureServer(preset, SuggestedJvm, true);
                ServerFiles.Create(preset); state.Servers.Add(preset); serverPreset = preset; LauncherStorage.Save(state); ShowPage("Serveurs");
            }
            catch (Exception ex) { ShowError(ex); }
        });
        ToolbarButton(bar, T("pack.create"), 215, async () =>
        {
            if (!ResolveDrafts()) return;
            using var file = new OpenFileDialog { Filter = "PZ runtime pack (pz-runtime-pack.json)|pz-runtime-pack.json", Title = T("pack.create") };
            if (file.ShowDialog(this) != DialogResult.OK) return;
            using var dialog = BuildProfileDialog(out var name);
            dialog.Text = T("pack.create"); name.PlaceholderText = "modded-server";
            foreach (var label in Descendants(dialog).OfType<Label>()) label.Text = label.Font.Size > 12 ? T("pack.create") : T("server.nameHelp");
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string manifest = file.FileName, installation = state.Installation, serverName = name.Text.Trim(); bool steamMode = profile.Steam;
            ServerPreset? created = null;
            await WorldWork(panel, () =>
            {
                created = ServerRuntimePack.Create(manifest, installation, serverName, Path.Combine(LauncherStorage.Root, "server-caches"), steamMode);
                return created.CachePath;
            });
            if (created != null)
            {
                HardwareAdvisor.ConfigureServer(created, SuggestedJvm, true);
                state.Servers.Add(created); serverPreset = created; LauncherStorage.Save(state); ShowPage("Serveurs");
            }
        });
        ToolbarButton(bar, T("server.folder"), 210, () => { if (serverPreset != null) OpenFolder(serverPreset.ConfigDirectory); });
        var tabs = new ThemedTabs { Dock = DockStyle.Fill };
        panel.Controls.Add(tabs); panel.Controls.Add(bar);
        choices.SelectedIndexChanged += (_, _) =>
        {
            if (choices.SelectedItem is not ServerPreset selected || selected == serverPreset) return;
            if (!ResolveDrafts()) { choices.SelectedItem = serverPreset; return; }
            serverPreset = selected; ShowPage("Serveurs");
        };
        if (serverPreset == null) { panel.Controls.Add(Theme.Label(T("server.empty"), 12)); return; }
        var preset = serverPreset;
        HardwareAdvisor.ConfigureServer(preset, SuggestedJvm);
        var launch = new Panel { Text = T("tab.launch") }; var config = new Panel { Text = T("server.files") }; var modPage = new Panel { Text = T("nav.mods") }; var console = new Panel { Text = T("server.console") };
        foreach (var tab in new[] { launch, config, modPage, console }) tabs.TabPages.Add(tab);
        var javaPage = new Panel { Text = T("java.tab") }; tabs.TabPages.Add(javaPage);
        javaPage.VisibleChanged += (_, _) =>
        {
            if (!javaPage.Visible) return;
            foreach (Control child in javaPage.Controls.Cast<Control>().ToArray()) child.Dispose();
            BuildJavaMods(javaPage, preset);
        };
        var clientsPage = new Panel { Text = T("runtime.clients") }; tabs.TabPages.Add(clientsPage);
        BuildRuntimeClients(clientsPage, preset);
        var original = ServerFiles.Read(preset); var edited = new Dictionary<string,string>(original);
        var inputs = new Dictionary<string,TextBox>();
        var fieldEditors = new List<ConfigurationFields>();
        GameConfigurationSchema? schema = null;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        launch.Controls.Add(flow); int y = 0;
        var intro = Theme.Label(T("server.note"), 9, color: Theme.Muted); intro.Size = new Size(720, 42); flow.Controls.Add(intro);
        if (preset.RuntimePackSha256.Length > 0)
        {
            var packLabel = Theme.Label(T("pack.active", preset.RuntimePackSha256[..Math.Min(12, preset.RuntimePackSha256.Length)]), 10, true);
            packLabel.Name = "ServerRuntimePack"; packLabel.Size = new Size(720, 42); flow.Controls.Add(packLabel);
            tips.SetToolTip(packLabel, ServerRuntimePack.Manifest(preset));
        }
        bool gog = InstallationLocator.IsGog(state.Installation);
        var steam = Check(T("steam.use"), preset.Steam && !gog); steam.Enabled = !gog;
        AddRuntimeRow(flow, steam, T(gog ? "install.gogMode" : "option.steamTip"), ref y);
        NumericUpDown Heap(string label, int value, int min)
        {
            var number = new NumericUpDown { Minimum = min, Maximum = 131072, Increment = 512, Value = Math.Clamp(value, min, 131072), Width = 200, BackColor = Theme.Surface, ForeColor = Theme.Text, ThousandsSeparator = true };
            AddEditorRow(flow, label, T("jvm.adviceTip"), number, ref y); number.ValueChanged += (_, _) => serverDirty = true; return number;
        }
        var xms = Heap(T("jvm.xms"), preset.XmsMb, 256); var xmx = Heap(T("option.memory"), preset.XmxMb, 1024);
        var collector = new ComboBox(); StyleCombo(collector); collector.Items.AddRange(["ZGC", "G1"]); collector.SelectedItem = preset.Collector;
        AddEditorRow(flow, T("option.collector"), T("option.collectorTip"), collector, ref y);
        NumericUpDown Tune(string key, int value, int minimum, int maximum, int step)
        {
            var number = new NumericUpDown { Minimum = minimum, Maximum = maximum, Increment = step, Value = Math.Clamp(value, minimum, maximum), BackColor = Theme.Surface, ForeColor = Theme.Text };
            AddEditorRow(flow, T(key), T(key + "Tip"), number, ref y); number.ValueChanged += (_, _) => serverDirty = true; return number;
        }
        var stack = Tune("jvm.stack", preset.StackKb, 256, 16384, 256);
        var pause = Tune("jvm.pause", preset.PauseTargetMs, 10, 2000, 25);
        var dedup = Check(T("jvm.dedup"), preset.StringDeduplication); AddRuntimeRow(flow, dedup, T("jvm.dedupTip"), ref y);
        dedup.CheckedChanged += (_, _) => serverDirty = true;
        void CollectorChanged() { pause.Enabled = dedup.Enabled = collector.Text == "G1"; }
        collector.SelectedIndexChanged += (_, _) => CollectorChanged(); CollectorChanged();
        var password = new TextBox { UseSystemPasswordChar = true, BackColor = Theme.Surface, ForeColor = Theme.Text };
        AddEditorRow(flow, T("server.adminPassword"), T("server.adminHelp"), password, ref y);
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(300, 120), Width = 740 };
        flow.Controls.Add(buttons);
        var stateLabel = Theme.Label("", 10, true, Theme.Muted); stateLabel.Size = new Size(710, 30); flow.Controls.Add(stateLabel);
        var cacheLabel = Theme.Label(preset.CachePath, 9, color: Theme.Muted); cacheLabel.Size = new Size(720, 30); flow.Controls.Add(cacheLabel); tips.SetToolTip(cacheLabel, preset.CachePath);
        flow.SizeChanged += (_, _) => { foreach (Control child in flow.Controls) child.Width = Math.Max(300, flow.ClientSize.Width - 26); };
        steam.CheckedChanged += (_, _) => serverDirty = true; collector.SelectedIndexChanged += (_, _) => serverDirty = true;
        ServerPreset Updated() => new() { Id = preset.Id, Java = preset.Java, ClientAddress = preset.ClientAddress, Name = preset.Name, CachePath = preset.CachePath, Steam = gog ? preset.Steam : steam.Checked, XmsMb = (int)xms.Value, XmxMb = (int)xmx.Value, Collector = collector.Text, StackKb = (int)stack.Value, PauseTargetMs = (int)pause.Value, StringDeduplication = dedup.Checked, RuntimePackSha256 = preset.RuntimePackSha256 };
        void Command(string command)
        {
            if (serverSession?.Preset != preset || !serverSession.Running) throw new IOException(T("server.notRunning"));
            serverSession.Command(command);
        }
        void PullEdits() { foreach (var pair in inputs) edited[pair.Key] = pair.Value.Text; }
        bool Save()
        {
            if (!serverDirty) return true;
            try
            {
                foreach (var editor in fieldEditors.Where(e => !e.IsDisposed)) editor.Commit();
                var next = Updated(); next.Validate(); PullEdits();
                if (schema != null)
                {
                    foreach (var option in schema.Server)
                    {
                        var before = ServerFiles.IniValues(original[".ini"]); var after = ServerFiles.IniValues(edited[".ini"]);
                        if (after.TryGetValue(option.Name, out var value) && value != before.GetValueOrDefault(option.Name)) option.Validate(value);
                    }
                    if (edited["_SandboxVars.lua"] != original["_SandboxVars.lua"])
                    {
                        
                        LuaConfigDocument? document = null;
                        try { document = new(edited["_SandboxVars.lua"]); } catch (InvalidDataException) { }
                        if (document != null) foreach (var option in schema.Sandbox)
                            if (document.Values.TryGetValue(option.Name, out var value)) option.Validate(value.Value);
                    }
                }
                ServerFiles.Save(next, original, edited);
                preset.Steam = next.Steam; preset.XmsMb = next.XmsMb; preset.XmxMb = next.XmxMb; preset.Collector = next.Collector;
                preset.StackKb = next.StackKb; preset.PauseTargetMs = next.PauseTargetMs; preset.StringDeduplication = next.StringDeduplication;
                LauncherStorage.Save(state); serverDirty = false; return true;
            }
            catch (Exception ex) { ShowError(ex); return false; }
        }
        saveServerDraft = Save;
        ToolbarButton(buttons, T("settings.save"), 205, () => Save(), true);
        ToolbarButton(buttons, T("jvm.apply"), 325, () =>
        {
            var suggested = SuggestedJvm; xms.Value = suggested.XmsMb; xmx.Value = suggested.XmxMb;
            stack.Value = suggested.StackKb; pause.Value = suggested.PauseTargetMs; collector.SelectedItem = suggested.Collector;
            dedup.Checked = suggested.StringDeduplication; serverDirty = true;
        });
        ToolbarButton(buttons, T("launch.preview"), 270, () =>
        {
            try { ShowLocalizedMessage(ServerFiles.Plan(state.Installation, Updated(), password.Text).CommandLinePreview, T("launch.preview"), false); } catch (Exception ex) { ShowError(ex); }
        });
        ToolbarButton(buttons, ServerExportTitle, 240, async () => { if (!worldBusy && Save()) await ExportServerScripts(preset, password.Text); });
        ToolbarButton(buttons, T("world.backup"), 170, async () => { if (Save()) await WorldWork(panel, () => ServerFiles.Backup(preset)); });
        ToolbarButton(buttons, T("server.start"), 205, async () =>
        {
            if (worldBusy || !Save()) return;
            worldBusy = true; panel.Enabled = false; UpdateFooter();
            try
            {
                if (serverSession is { Running: true }) throw new IOException(T("server.alreadyRunning"));
                
                if (!File.Exists(Path.Combine(preset.CachePath, "db", preset.Name + ".db")) && password.Text.Length == 0) throw new IOException(T("server.passwordRequired"));
                var values = ServerFiles.IniValues(File.ReadAllText(preset.Ini));
                var serverProfile = new PlayerProfile { CachePath = preset.CachePath, Steam = preset.Steam };
                var catalog = ModCatalog.Scan(serverProfile, InstallationLocator.ReadGameVersion(state.Installation));
                var ids = values.GetValueOrDefault("Mods", "").Replace("\\", "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var resolved = ModResolver.Resolve(catalog, ids);
                if (!resolved.Success) throw new IOException(string.Join("\n", resolved.Issues));
                if (!resolved.Ordered.SequenceEqual(ids)) throw new IOException(T("server.orderFirst"));
                string secret = password.Text;
                var plan = await Task.Run(() => ServerFiles.Plan(state.Installation, preset, secret));
                if (preset.Java.JavaLoaderEnabled)
                {
                    var p = preset.JavaProfile();
                    var java = JavaMods.Prepare(p, JavaMods.Scan(p, catalog, ids, state.Installation, "server"), state.Installation, "server");
                    await JavaMods.PreflightAsync(plan, java);
                }
                LauncherStorage.Log(plan.CommandLinePreview);
                var nextSession = new ServerSession(plan, preset, password.Text); serverSession?.Dispose(); serverSession = nextSession; password.Clear(); tabs.SelectedIndex = 3;
            }
            catch (Exception ex) { ShowError(ex); }
            finally { worldBusy = false; if (!panel.IsDisposed) panel.Enabled = true; UpdateFooter(); }
        }, true);
        ToolbarButton(buttons, T("server.stop"), 215, () =>
        { try { Command("quit"); } catch (Exception ex) { ShowError(ex); } });
        ToolbarButton(buttons, T("world.restore"), 185, async () =>
        {
            if (!ResolveDrafts()) return;
            using var file = new OpenFileDialog { Filter = "ZIP (*.zip)|*.zip", InitialDirectory = Path.Combine(LauncherStorage.Root, "backups", "servers", preset.Name) };
            if (file.ShowDialog(this) != DialogResult.OK) return;
            ServerPreset? restored = null;
            await WorldWork(panel, () => { restored = ServerFiles.Restore(file.FileName, Path.Combine(LauncherStorage.Root, "restored-server-caches")); return restored.CachePath; });
            if (restored != null) { state.Servers.Add(restored); serverPreset = restored; LauncherStorage.Save(state); ShowPage("Serveurs"); }
        });
        var fileTabs = new ThemedTabs { Dock = DockStyle.Fill }; config.Controls.Add(fileTabs);
        var fieldsPages = new Dictionary<string, Panel>();
        void BuildFields(string suffix)
        {
            var fields = fieldsPages[suffix]; foreach (Control c in fields.Controls.Cast<Control>().ToArray()) c.Dispose(); fields.Controls.Clear();
            try
            {
                string contents = inputs[suffix].Text;
                var lua = suffix == ".ini" ? null : new LuaConfigDocument(contents);
                var values = suffix == ".ini" ? ServerFiles.IniValues(contents) : lua!.Values.ToDictionary(p => p.Key, p => p.Value.Value);
                IEnumerable<ConfigOption> options = suffix == ".ini" ? schema?.Server ?? [] : suffix == "_SandboxVars.lua" ? schema?.Sandbox ?? [] : [];
                if (lua != null)
                {
                    var known = options.Select(o => o.Name).ToHashSet();
                    options = options.Concat(lua.Values.Values.Where(v => !known.Contains(v.Name)).Select(v => new ConfigOption { Name = v.Name, Type = v.Type, Default = v.Value })).ToArray();
                }
                var editor = new ConfigurationFields(options, values, (name, value, type) =>
                {
                    string current = inputs[suffix].Text;
                    if (suffix == ".ini") inputs[suffix].Text = ServerFiles.UpdateValue(current, name, value);
                    else
                    {
                        if (string.IsNullOrWhiteSpace(current) && suffix == "_SandboxVars.lua" && schema?.SandboxVersion > 0) current = "SandboxVars = {\n    VERSION = " + schema.SandboxVersion + ",\n}\n";
                        inputs[suffix].Text = new LuaConfigDocument(current).Set(name, value, type);
                    }
                });
                fieldEditors.RemoveAll(e => e.IsDisposed); fieldEditors.Add(editor); fields.Controls.Add(editor);
            }
            catch (Exception ex) { var message = Readout(ex.Message + "\r\n\r\n" + T("config.rawHelp")); fields.Controls.Add(message); }
        }
        foreach (string suffix in ServerFiles.Suffixes)
        {
            var tab = new Panel { Text = suffix == ".ini" ? "INI" : suffix[1..^4] }; fileTabs.TabPages.Add(tab);
            var text = Readout(edited[suffix]); text.ReadOnly = false; text.AcceptsTab = true; text.Font = new Font("Consolas", 9.5f); inputs[suffix] = text;
            text.TextChanged += (_, _) => serverDirty = true;
            var views = new ThemedTabs { Dock = DockStyle.Fill }; tab.Controls.Add(views);
            var fields = new Panel { Text = T("config.fields") }; fieldsPages[suffix] = fields;
            var source = new Panel { Text = T("config.raw") }; source.Controls.Add(text); views.TabPages.Add(fields); views.TabPages.Add(source);
            fields.Controls.Add(Readout(T("config.loading")));
            fields.VisibleChanged += (_, _) => { if (fields.Visible && schema != null) BuildFields(suffix); };
        }
        async Task LoadSchema()
        {
            try
            {
                schema = await GameConfigurationSchema.Load(state.Installation);
                if (!panel.IsDisposed) foreach (string suffix in ServerFiles.Suffixes) BuildFields(suffix);
            }
            catch (Exception ex)
            {
                LauncherStorage.Log(ex.ToString());
                if (!panel.IsDisposed) foreach (var fields in fieldsPages.Values)
                { fields.Controls.Clear(); fields.Controls.Add(Readout(ex.Message + "\r\n" + T("config.rawHelp"))); }
            }
        }
        configurationTask = LoadSchema();
        var saveBar = new ResponsiveToolbar { Dock = DockStyle.Bottom, Height = 45 }; config.Controls.Add(saveBar);
        ToolbarButton(saveBar, T("settings.save"), 210, () => Save(), true);
        ToolbarButton(saveBar, T("server.reference"), 190, () => OpenUrl("https://pzwiki.net/wiki/Server_settings"));
        var hint = Theme.Label(T("server.filesHelp"), 8, color: Theme.Muted); hint.Width = 360; hint.Height = 40; saveBar.Controls.Add(hint);
        var modTools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 77, WrapContents = true };
        var modHelp = Theme.Label(T("server.modsHelp"), 9, color: Theme.Muted); modHelp.Width = 750; modHelp.Height = 33; modTools.Controls.Add(modHelp); modTools.SetFlowBreak(modHelp, true);
        var modGrid = Theme.Grid(); modGrid.RowTemplate.Height = 30;
        modGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = T("column.enabled"), Width = 70 });
        modGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("column.mod"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        modGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Workshop ID", Width = 170, ReadOnly = true });
        modPage.Controls.Add(modGrid); modPage.Controls.Add(modTools);
        var serverMods = ModCatalog.Scan(new PlayerProfile { CachePath = preset.CachePath, Steam = steam.Checked }, InstallationLocator.ReadGameVersion(state.Installation));
        var chosen = ServerFiles.IniValues(edited[".ini"]).GetValueOrDefault("Mods", "").Replace("\\", "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var mod in serverMods)
        {
            int i = modGrid.Rows.Add(chosen.Contains(mod.Id), mod.Name, mod.WorkshopId); modGrid.Rows[i].Tag = mod;
        }
        modGrid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || e.Graphics == null) return;
            e.PaintBackground(e.ClipBounds, true); int size = modGrid.LogicalToDeviceUnits(18);
            Theme.DrawCheck(e.Graphics, new Rectangle(e.CellBounds.X + (e.CellBounds.Width - size) / 2, e.CellBounds.Y + (e.CellBounds.Height - size) / 2, size, size), Convert.ToBoolean(e.Value), true);
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus); e.Handled = true;
        };
        modGrid.CurrentCellDirtyStateChanged += (_, _) => { if (modGrid.IsCurrentCellDirty) modGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        bool filling = false;
        void RefreshServerMods()
        {
            serverMods = ModCatalog.Scan(new PlayerProfile { CachePath = preset.CachePath, Steam = steam.Checked }, InstallationLocator.ReadGameVersion(state.Installation));
            chosen = ServerFiles.IniValues(inputs[".ini"].Text).GetValueOrDefault("Mods", "").Replace("\\", "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            foreach (string missing in chosen.Where(id => serverMods.All(m => m.Id != id))) serverMods.Add(new() { Id = missing, Name = missing + " · " + T("mods.missing"), Available = false });
            filling = true; modGrid.Rows.Clear();
            foreach (var mod in serverMods)
            {
                int row = modGrid.Rows.Add(chosen.Contains(mod.Id), mod.Name, mod.WorkshopId); modGrid.Rows[row].Tag = mod;
                if (!mod.Available) modGrid.Rows[row].DefaultCellStyle.ForeColor = Theme.Muted;
            }
            filling = false;
        }
        modPage.VisibleChanged += (_, _) => { if (modPage.Visible) RefreshServerMods(); };
        void ApplyMods(IEnumerable<string> requested)
        {
            var result = ModResolver.Resolve(serverMods, requested);
            if (!result.Success) throw new IOException(string.Join("\n", result.Issues));
            chosen = result.Ordered; var workshop = chosen.Select(id => serverMods.First(m => m.Id == id).WorkshopId).Where(id => id.Length > 0).Distinct();
            inputs[".ini"].Text = ServerFiles.UpdateValue(ServerFiles.UpdateValue(inputs[".ini"].Text, "Mods", string.Join(';', chosen)), "WorkshopItems", string.Join(';', workshop));
            filling = true; foreach (DataGridViewRow row in modGrid.Rows) if (row.Tag is InstalledMod mod) row.Cells[0].Value = chosen.Contains(mod.Id); filling = false;
            serverDirty = true;
        }
        modGrid.CellValueChanged += (_, e) =>
        {
            if (filling || e.RowIndex < 0 || e.ColumnIndex != 0 || modGrid.Rows[e.RowIndex].Tag is not InstalledMod mod) return;
            try
            {
                var next = chosen.ToList(); bool active = Convert.ToBoolean(modGrid.Rows[e.RowIndex].Cells[0].Value);
                if (active) next.Add(mod.Id);
                else
                {
                    var dependant = serverMods.FirstOrDefault(m => chosen.Contains(m.Id) && m.Requires.Contains(mod.Id));
                    if (dependant != null) throw new IOException(T("error.dependent", dependant.Name)); next.Remove(mod.Id);
                }
                if (!active && !ModResolver.Resolve(serverMods, next).Success)
                {
                    
                    chosen = next; inputs[".ini"].Text = ServerFiles.UpdateValue(inputs[".ini"].Text, "Mods", string.Join(';', next)); serverDirty = true;
                }
                else ApplyMods(next);
            }
            catch (Exception ex) { ShowError(ex); filling = true; modGrid.Rows[e.RowIndex].Cells[0].Value = chosen.Contains(mod.Id); filling = false; }
        };
        ToolbarButton(modTools, T("server.useProfileMods"), 265, () => { try { ApplyMods(selection.Ids); } catch (Exception ex) { ShowError(ex); } });
        ToolbarButton(modTools, T("mods.auto"), 160, () => { try { ApplyMods(chosen); } catch (Exception ex) { ShowError(ex); } });
        ToolbarButton(modTools, T("settings.save"), 205, () => Save(), true);
        var output = Readout(T("server.idle")); console.Controls.Add(output);
        var consoleBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46 }; console.Controls.Add(consoleBar);
        var commandBox = new TextBox { Width = 270, BackColor = Theme.Surface, ForeColor = Theme.Text, PlaceholderText = "help" }; consoleBar.Controls.Add(commandBox);
        ToolbarButton(consoleBar, T("server.send"), 140, () => { try { Command(commandBox.Text); commandBox.Clear(); } catch (Exception ex) { ShowError(ex); } });
        ToolbarButton(consoleBar, T("server.saveWorld"), 170, () => { try { Command("save"); } catch (Exception ex) { ShowError(ex); } });
        ToolbarButton(consoleBar, T("server.stop"), 150, () => { try { Command("quit"); } catch (Exception ex) { ShowError(ex); } });
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        void UpdateServer()
        {
            bool same = serverSession?.Preset == preset; bool running = serverSession is { Running: true };
            consoleBar.Enabled = same && running;
            stateLabel.Text = running ? T("server.running", serverSession!.Preset.Name) : T("server.idle");
            stateLabel.ForeColor = running ? Theme.Success : Theme.Muted;
            if (same && serverSession != null && output.Text != serverSession.Output) { output.Text = serverSession.Output; output.SelectionStart = output.TextLength; output.ScrollToCaret(); }
        }
        timer.Tick += (_, _) => UpdateServer(); timer.Start(); panel.Disposed += (_, _) => timer.Dispose(); UpdateServer(); serverDirty = false;
    }
}
