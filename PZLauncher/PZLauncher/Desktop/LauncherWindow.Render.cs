using System.Drawing.Imaging;
using System.Text.Json;

namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    
    private async Task ExportUiRenderings(string directory)
    {
        Directory.CreateDirectory(directory);
        var exports = new List<object>();
        var issues = new List<object>();
        string previousLanguage = L.Code;
        Size previousSize = ClientSize;
        string previousPage = page;
        var originalSaves = saves; var originalServers = state.Servers; var originalServer = serverPreset; string originalWorld = selectedWorld, originalLog = diagnosticPath;
        var originalFavorites = state.OnlineFavorites; var originalProfiles = state.Profiles; var originalOnlineDraft = onlineDraft;
        string originalOnlineId = onlineSelectedId; var originalPublic = onlinePublic; string originalOnlineResult = onlineResult;
        
        string fixtureRoot = Path.Combine(Path.GetTempPath(), "PZLauncher-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        string fixtureGog = Path.Combine(fixtureRoot, "GOG Games", "Project Zomboid");
        Directory.CreateDirectory(Path.Combine(fixtureGog, "jre64", "bin"));
        File.WriteAllText(Path.Combine(fixtureGog, "jre64", "bin", "java.exe"), "render fixture, never executed");
        File.WriteAllText(Path.Combine(fixtureGog, "projectzomboid.jar"), "render fixture");
        File.WriteAllText(Path.Combine(fixtureGog, "ProjectZomboid64.json"), "{}");
        File.WriteAllText(Path.Combine(fixtureGog, "goggame-1234567890.info"), "{}");
        state.Profiles = [.. state.Profiles]; state.OnlineFavorites = [];
        var fixtureFavorite = new OnlineFavorite { Name = "Community survival", Host = "203.0.113.17", Port = 16261, QueryPort = 16261,
            ModIds = ["ExampleMod", "ExampleDependency"], WorkshopIds = ["123456789", "234567890"], ModListConfirmed = true,
            LastInfo = new() { Name = "Community survival", Host = "203.0.113.17", Port = 16261, QueryPort = 16261, Players = 12, MaxPlayers = 32,
                Version = "42.20.0", Map = "Muldraugh, KY", Ping = 36, Responded = true, CheckedAt = DateTimeOffset.Now,
                Rules = new() { ["mods"] = "ExampleMod;ExampleDependency", ["modCount"] = "2", ["description"] = "Community survival\r\nCooperative survival, weekly events.\r\nWelcome to our server!", ["pvp"] = "false", ["version"] = "42.20.0" } } };
        OnlineProfiles.Save(state, fixtureFavorite, new PlayerProfile { CachePath = fixtureRoot }, fixtureRoot);
        onlineSelectedId = fixtureFavorite.Id; onlineDraft = CloneFavorite(fixtureFavorite); onlineResult = "";
        onlinePublic = [fixtureFavorite.LastInfo];
        string fixtureWorld = ManagementVerification.CreateWorld(Path.Combine(fixtureRoot, "Saves", "Sandbox", "Review world"));
        UsabilityVerification.CreateWorldFiles(fixtureWorld);
        saves = [new SaveEntry("Review world", "Sandbox", fixtureWorld, DateTime.Now, 4)]; selectedWorld = fixtureWorld;
        var fixtureServer = ServerRuntimePack.Create(ServerJavaVerification.Pack(Path.Combine(fixtureRoot, "packs"), state.Installation, "1"), state.Installation, "community-server", Path.Combine(fixtureRoot, "servers"), false);
        state.Servers = [fixtureServer]; serverPreset = fixtureServer;
        state.Profiles.Add(RuntimeClients.Create(fixtureServer, state.Installation, "Community client", Path.Combine(fixtureRoot, "clients")));
        File.WriteAllText(Path.Combine(fixtureServer.ConfigDirectory, fixtureServer.Name + "_SandboxVars.lua"), "SandboxVars={ VERSION=6, Zombies=4, ZombieLore={Speed=2}, }\n");
        File.WriteAllText(Path.Combine(fixtureServer.ConfigDirectory, fixtureServer.Name + "_spawnregions.lua"), "function SpawnRegions() return { {name=\"Rosewood\", file=\"media/maps/Rosewood, KY/spawnpoints.lua\"}, } end\n");
        File.WriteAllText(Path.Combine(fixtureServer.ConfigDirectory, fixtureServer.Name + "_spawnpoints.lua"), "function SpawnPoints() return { unemployed={{worldX=35,worldY=25,posX=100,posY=150,posZ=0}}, } end\n");
        diagnosticPath = Path.Combine(fixtureRoot, "console.txt");
        File.WriteAllText(diagnosticPath, "ERROR: General, 0> t:1234567890123> java.lang.NoSuchMethodError: example.RenderHook\n at example.Mod.render(Mod.java:12)\n /mods/ExampleMod/media/lua/client/render.lua\n\nWARN : General, 0> t:1234567890999> required mod missing: ExampleDependency\n");
        try
        {
            foreach (var language in L.Languages)
            {
                ApplyLanguage(language.Code, false);
                foreach (var size in new[] { new Size(1280, 800), new Size(1080, 700) })
                {
                    Size = size;
                    foreach (string view in new[] { "Jouer", "Online", "Mods", "Sauvegardes", "Serveurs", "Diagnostic", "Réglages", "Actualités", "À propos" })
                    {
                        if (view == "Online") onlineTabIndex = 0;
                        ShowPage(view); PerformLayout(); Update();
                        if (view == "Sauvegardes") await worldScanTask;
                        if (view == "Diagnostic") await diagnosticTask;
                        if (view == "Serveurs") await configurationTask;
                        string filename = view switch { "Online" => "online", "Réglages" => "settings", "Actualités" => "news", "À propos" => "about", "Sauvegardes" => "saves", "Mods" => "mods", "Serveurs" => "servers", "Diagnostic" => "logs", _ => "home" };
                        Capture(this, language.Code + "-" + size.Width + "-" + filename);
                        if (view == "À propos")
                        {
                            var credits = Descendants(pageHost).OfType<FlowLayoutPanel>().Single(c => c.Name == "CommunityCredits");
                            credits.ScrollControlIntoView(credits.Controls[^1]);
                            Capture(this, language.Code + "-" + size.Width + "-about-links");
                        }
                        if (view is "Online" or "Réglages" or "Mods" or "Sauvegardes" or "Serveurs" or "Diagnostic")
                        {
                            var tabs = Descendants(pageHost).OfType<ThemedTabs>().First();
                            for (int index = 1; index < tabs.TabPages.Count; index++)
                            {
                                tabs.SelectedIndex = index; tabs.PerformLayout(); Update();
                                Capture(this, language.Code + "-" + size.Width + "-" + filename + "-" + index);
                                if (view == "Serveurs" && index == 1)
                                {
                                    var files = Descendants(tabs.TabPages[index]).OfType<ThemedTabs>().First();
                                    for (int file = 0; file < files.TabPages.Count; file++)
                                    {
                                        files.SelectedIndex = file; PerformLayout(); Update();
                                        Capture(this, language.Code + "-" + size.Width + "-server-file-" + file);
                                    }
                                }
                                if (view == "Online" && index == 1)
                                {
                                    var loading = Descendants(pageHost).OfType<ProgressBar>().Single(c => c.Name == "ServerLoading");
                                    var status = Descendants(pageHost).OfType<Label>().Single(c => c.Name == "ServerLoadingStatus");
                                    loading.Visible = true; string previous = status.Text;
                                    var refreshServers = Descendants(pageHost).OfType<ActionButton>().Single(c => c.Name == "RefreshPublicServers");
                                    refreshServers.Text = T("dialog.cancel");
                                    status.Text = T("online.searchProgress", 1, 1382, 3);
                                    Capture(this, language.Code + "-" + size.Width + "-online-loading");
                                    loading.Visible = false; status.Text = previous; refreshServers.Text = T("online.refresh");
                                    using var details = new OnlineServerDialog(fixtureFavorite.LastInfo!, "", CancellationToken.None, false);
                                    details.Show(this); details.PerformLayout(); details.Update();
                                    var detailTabs = Descendants(details).OfType<ThemedTabs>().Single();
                                    for (int detailTab = 0; detailTab < detailTabs.TabCount; detailTab++)
                                    {
                                        detailTabs.SelectedIndex = detailTab;
                                        Capture(details, language.Code + "-" + size.Width + "-online-details-" + detailTab);
                                    }
                                    details.Close();
                                    using var probe = new ServerProbeDialog("", fixtureFavorite.LastInfo!, CancellationToken.None);
                                    probe.Show(this); probe.PerformLayout(); probe.Update();
                                    Capture(probe, language.Code + "-" + size.Width + "-online-grab-mods"); probe.Close();
                                    using var statistics = new OnlineStatisticsDialog(ServerStatisticsVerification.Fixtures(), true, "", CancellationToken.None);
                                    statistics.ClientSize = size.Width == 1080 ? new(874, 611) : new(1000, 740);
                                    statistics.Show(this); statistics.PerformLayout(); statistics.Update();
                                    var statisticsTabs = Descendants(statistics).OfType<ThemedTabs>().Single();
                                    for (int statisticsTab = 0; statisticsTab < statisticsTabs.TabCount; statisticsTab++)
                                    {
                                        statisticsTabs.SelectedIndex = statisticsTab;
                                        Capture(statistics, language.Code + "-" + size.Width + "-online-statistics-" + statisticsTab);
                                    }
                                    statistics.Close();
                                    var capturedServer = System.Text.Json.JsonSerializer.Deserialize<OnlineServerInfo>(System.Text.Json.JsonSerializer.Serialize(fixtureFavorite.LastInfo))!;
                                    capturedServer.FullMods = new() { GameVersion = "42.20", CapturedAt = DateTimeOffset.Now,
                                        Mods = [new("FixtureOne", "1234567890", "Fixture Workshop mod"), new("FixtureTwo", "", "Fixture local mod")],
                                        Workshop = [new("1234567890", 1760000000)] };
                                    using var capturedDetails = new OnlineServerDialog(capturedServer, "", CancellationToken.None, false);
                                    capturedDetails.Show(this); Descendants(capturedDetails).OfType<ThemedTabs>().Single().SelectedIndex = 1;
                                    Capture(capturedDetails, language.Code + "-" + size.Width + "-online-captured-mods"); capturedDetails.Close();
                                }
                                if (view == "Mods" && index == 1 || view == "Serveurs" && index == 4)
                                {
                                    var javaProfile = view == "Serveurs" ? fixtureServer.JavaProfile() : profile;
                                    string previousLoader = javaProfile.JavaLoader;
                                    try
                                    {
                                        foreach (string mode in JavaMods.Loaders)
                                        {
                                            javaProfile.JavaLoader = mode;
                                            var javaPage = tabs.TabPages[index];
                                            foreach (Control child in javaPage.Controls.Cast<Control>().ToArray()) child.Dispose();
                                            BuildJavaMods(javaPage, view == "Serveurs" ? fixtureServer : null); PerformLayout(); Update();
                                            Capture(this, language.Code + "-" + size.Width + (view == "Serveurs" ? "-server-java-" : "-java-") + mode);
                                        }
                                    }
                                    finally { javaProfile.JavaLoader = previousLoader; }
                                }
                                if (view == "Sauvegardes" && index == 1 && mapWindowHost != null)
                                {
                                    mapWindowHost.Detach();
                                    if (mapWindowHost.Window is Form detached)
                                    {
                                        detached.PerformLayout(); detached.Update(); Capture(detached, language.Code + "-" + size.Width + "-map-window"); detached.Close();
                                    }
                                }
                            }
                        }
                    }
                    string previousInstallation = state.Installation; var previousInstallations = installations;
                    var previousFtp = ftpWorld; string previousFtpWorld = selectedWorld;
                    try
                    {
                        ftpWorld = new FtpWorldWorkspace(Path.Combine(fixtureRoot, "ftp-preview"), "saves.example.test:21/Zomboid/Saves/Multiplayer/Community", true,
                            () => throw new InvalidOperationException("Rendering must not connect to FTP"));
                        if (!Directory.Exists(ftpWorld.LocalRoot))
                        { ManagementVerification.CreateWorld(ftpWorld.LocalRoot); UsabilityVerification.CreateWorldFiles(ftpWorld.LocalRoot); }
                        selectedWorld = ftpWorld.LocalRoot; ShowPage("Sauvegardes"); await worldScanTask;
                        var ftpTabs = Descendants(pageHost).OfType<ThemedTabs>().First();
                        for (int index = 0; index < ftpTabs.TabCount; index++)
                        { ftpTabs.SelectedIndex = index; PerformLayout(); Update(); Capture(this, language.Code + "-" + size.Width + "-ftp-world-" + index); }
                    }
                    finally { ftpWorld = previousFtp; selectedWorld = previousFtpWorld; }
                    try
                    {
                        installations = [new(fixtureGog, "gog", new(42, 20)), new(previousInstallation, "steam", new(42, 20)),
                            new(Path.Combine(fixtureRoot, "Second disk", "SteamLibrary", "ProjectZomboid"), "steam", new(41, 78))];
                        foreach (string variant in new[] { "gog", "missing" })
                        {
                            state.Installation = variant == "gog" ? fixtureGog : Path.Combine(fixtureRoot, "Disconnected disk", "ProjectZomboid");
                            ShowPage("Réglages"); UpdateFooter(); PerformLayout(); Update();
                            Capture(this, language.Code + "-" + size.Width + "-installation-" + variant);
                        }
                    }
                    finally { state.Installation = previousInstallation; installations = previousInstallations; }
                }
                using var dialog = CreateMessageDialog(T("draft.question"), T("draft.title"), true);
                dialog.Show(this); dialog.PerformLayout(); dialog.Update();
                Capture(dialog, language.Code + "-draft-dialog");
                dialog.Close();
                
                using var ftp = new FtpWorldDialog();
                ftp.Show(this); ftp.PerformLayout(); ftp.Update();
                Capture(ftp, language.Code + "-ftp-dialog-800");
                ftp.Size = new Size(720, 640); ftp.PerformLayout(); ftp.Update();
                Capture(ftp, language.Code + "-ftp-dialog-720");
                ftp.Close();
                using var exportDialog = BuildServerExportDialog(fixtureServer, true, out var exportFormat, out _, out _);
                exportDialog.Show(this);
                exportFormat.SelectedIndex = 2;
                foreach (int width in new[] { 686, 640 })
                {
                    exportDialog.Width = width; exportDialog.PerformLayout(); exportDialog.Update();
                    Capture(exportDialog, language.Code + "-server-export-" + width);
                }
                exportDialog.Close();
                using var worldEditor = new WorldSettingsDialog(fixtureWorld, fixtureRoot, state.Installation);
                worldEditor.Show(this); await worldEditor.Ready;
                foreach (var editorSize in new[] { new Size(900, 640), new Size(690, 500) })
                {
                    worldEditor.Size = editorSize;
                    var worldTabs = Descendants(worldEditor).OfType<ThemedTabs>().First();
                    for (int i = 0; i < worldTabs.TabPages.Count; i++)
                    { worldTabs.SelectedIndex = i; worldEditor.PerformLayout(); worldEditor.Update(); Capture(worldEditor, language.Code + "-world-editor-" + editorSize.Width + "-" + i); }
                }
                worldEditor.Close();
                using var profileDialog = BuildProfileDialog(out _);
                profileDialog.Show(this); profileDialog.PerformLayout(); profileDialog.Update();
                Capture(profileDialog, language.Code + "-profile-dialog");
                profileDialog.Close();
                using var jvmDialog = BuildJvmArgumentsDialog(out _, out _, out _);
                jvmDialog.Show(this); jvmDialog.PerformLayout(); jvmDialog.Update();
                Capture(jvmDialog, language.Code + "-jvm-arguments"); jvmDialog.Close();
                foreach (string loader in new[] { JavaMods.Leaf, JavaMods.ZombieBuddy })
                {
                    using var installer = new LoaderDownloadDialog(loader, Path.Combine(fixtureRoot, "My profile"), "", CancellationToken.None, _ => { }, false);
                    installer.Show(this); installer.PerformLayout(); installer.Update();
                    Capture(installer, language.Code + "-download-" + loader);
                    var progress = (ProgressBar)installer.Controls.Find("LoaderDownloadProgress", true).Single();
                    progress.Style = ProgressBarStyle.Continuous; progress.Value = 45; progress.Visible = true;
                    Capture(installer, language.Code + "-download-" + loader + "-progress");
                    installer.Close();
                }
            }
        }
        finally
        {
            saves = originalSaves; state.Servers = originalServers; serverPreset = originalServer; selectedWorld = originalWorld; diagnosticPath = originalLog;
            state.OnlineFavorites = originalFavorites; state.Profiles = originalProfiles; onlineDraft = originalOnlineDraft;
            onlineSelectedId = originalOnlineId; onlinePublic = originalPublic; onlineResult = originalOnlineResult;
            ClientSize = previousSize; ApplyLanguage(previousLanguage, false); ShowPage(previousPage);
        }
        File.WriteAllText(Path.Combine(directory, "rendering.json"), JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.Now, method = "WinForms DrawToBitmap; not a desktop screenshot",
            dpi = DeviceDpi, mods = mods.Count, saves = saves.Count, news = news.Count, exports, issues
        }, new JsonSerializerOptions { WriteIndented = true }));
        void Capture(Form form, string name)
        {
            
            form.Refresh();
            foreach (var combo in Descendants(form).OfType<ComboBox>().Where(c => c.Visible)) combo.Refresh();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            
            
            if (form == this)
            {
                using var graphics = Graphics.FromImage(bitmap);
                foreach (var handle in resizeHandles.Where(h => h.Visible))
                {
                    if (GetChildAtPoint(new(handle.Left + handle.Width / 2, handle.Top + handle.Height / 2)) != handle)
                        issues.Add(new { file = name, control = "ResizeHandleHidden", text = handle.Name });
                    using var overlay = new Bitmap(handle.Width, handle.Height);
                    handle.DrawToBitmap(overlay, new(Point.Empty, overlay.Size));
                    Point origin = handle.PointToScreen(Point.Empty);
                    graphics.DrawImageUnscaled(overlay, origin.X - Left, origin.Y - Top);
                }
            }
            bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
            exports.Add(new { file = name + ".png", width = form.Width, height = form.Height });
            foreach (var control in Descendants(form).Where(c => c.Visible && c.Dock == DockStyle.Fill && c.Parent != null))
                foreach (Control sibling in control.Parent!.Controls)
                    if (sibling != control && sibling.Visible && sibling.Dock is DockStyle.Top or DockStyle.Bottom && control.Bounds.IntersectsWith(sibling.Bounds))
                        issues.Add(new { file = name, control = "DockOverlap", text = control.GetType().Name + "/" + sibling.GetType().Name, bounds = control.Bounds.ToString(), sibling = sibling.Bounds.ToString() });
            if (form == this)
                foreach (var button in navigation.Values)
                {
                    if (button.Parent?.Controls.Find("NavigationHeading", false).FirstOrDefault() is Control heading
                        && button.Bounds.IntersectsWith(heading.Bounds))
                        issues.Add(new { file = name, control = "NavigationHeadingOverlap", text = button.Text });
                    if (button.Parent is Control sidebar
                        && sidebar.Controls.Cast<Control>().FirstOrDefault(c => c.Dock == DockStyle.Bottom) is Control footer
                        && button.Bounds.IntersectsWith(footer.Bounds))
                        issues.Add(new { file = name, control = "SidebarOverlap", text = button.Text, width = button.Width, height = button.Height });
                }
            foreach (var flow in Descendants(form).OfType<FlowLayoutPanel>().Where(c => c.Visible && !c.AutoScroll))
                foreach (Control child in flow.Controls)
                    if (child.Visible && (child.Right > flow.ClientSize.Width + 1 || child.Bottom > flow.ClientSize.Height + 1))
                        issues.Add(new { file = name, control = "ToolbarOverflow", text = child.Text, width = child.Width, height = child.Height });
            foreach (var flow in Descendants(form).OfType<FlowLayoutPanel>().Where(c => c.Visible && c.AutoScroll))
                if (flow.HorizontalScroll.Visible)
                    issues.Add(new { file = name, control = "FlowLayoutPanel", text = "Unexpected horizontal scrolling", width = flow.Width, height = flow.Height });
            foreach (var grid in Descendants(form).OfType<DataGridView>().Where(c => c.Visible))
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    var needed = TextRenderer.MeasureText(column.HeaderText, grid.ColumnHeadersDefaultCellStyle.Font,
                        new Size(Math.Max(1, column.Width - 22), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    if (needed.Height > grid.ColumnHeadersHeight - 6)
                        issues.Add(new { file = name, control = "GridHeader", text = column.HeaderText, width = column.Width, height = grid.ColumnHeadersHeight, neededHeight = needed.Height });
                }
            foreach (Control control in Descendants(form).Where(c => c.Visible && c.Width > 0 && c.Height > 0 && c.Text.Length > 0))
            {
                bool fixedCopy = L.Catalog(L.Code).Values.Contains(control.Text) || L.Catalog("en").Values.Contains(control.Text)
                    || control is ActionButton && control.Text.Length <= 3;
                if (!fixedCopy) continue; 
                Size area;
                TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
                if (control is ActionButton button) { area = button.TextArea.Size; flags |= TextFormatFlags.NoPadding; }
                else if (control is Label) area = control.ClientSize;
                else if (control is CheckBox) { area = new(control.Width - 24, control.Height); flags |= TextFormatFlags.NoPadding; }
                else continue;
                Size needed = TextRenderer.MeasureText(control.Text, control.Font, new Size(Math.Max(1, area.Width), int.MaxValue), flags);
                if (needed.Height > area.Height + 1 || needed.Width > area.Width + 1)
                    issues.Add(new { file = name, control = control.GetType().Name, text = control.Text, width = area.Width, height = area.Height, neededWidth = needed.Width, neededHeight = needed.Height });
            }
        }
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }
}
