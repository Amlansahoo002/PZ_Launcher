using System.Diagnostics;
using System.Globalization;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow : Form
{
    private readonly LauncherState state;
    private readonly bool rendering;
    private readonly string? launchVerificationDirectory;
    private StreamWriter? launchVerificationOutput;
    private readonly object launchOutputLock = new();
    private PlayerProfile profile;
    private readonly NewsService newsService = new();
    private readonly GameLaunchService launchService = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, Image> images = [];
    private readonly Dictionary<string, ActionButton> navigation = [];
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill, Padding = new Padding(28, 4, 28, 20) };
    private readonly ComboBox profileCombo = new();
    private readonly ComboBox languageCombo = new() { Name = "LauncherLanguage" };
    private readonly Label pageTitle = Theme.Label(T("title.home"), 23, true);
    private readonly Label footerStatus = Theme.Label(T("status.search"), 10);
    private readonly Label footerDetail = Theme.Label("", 9, color: Theme.Muted);
    private readonly ActionButton play = new(T("status.play")) { Primary = true, IconCode = 0 };
    private readonly ToolTip tips = new() { AutoPopDelay = 14000, InitialDelay = 400 };
    private List<InstalledMod> mods = [];
    private List<string> modScanIssues = [];
    private List<SaveEntry> saves = [];
    private IReadOnlyList<NewsItem> news = [];
    private ModSelection selection;
    private string page = "Jouer";
    private bool switchingProfile;
    private bool modsDirty;
    private bool settingsDirty;
    private int refreshGeneration;
    private Process? game;
    private HeroCard? hero;
    private Label? collectionStatus;
    private Label? savesStatus;
    private DataGridView? modGrid;
    private DataGridView? savesGrid;
    private TextBox? modSearch;
    private ComboBox? modFilter;
    private bool fillingMods;
    private bool libraryReady;
    private OptionDocument? options;
    private readonly Dictionary<string, Func<string>> optionReaders = [];
    private readonly Dictionary<string, string> optionInitial = [];

    private readonly string? expectedLaunchCache;
    internal LauncherWindow(string? renderDirectory = null, bool verification = false, string? verifyLaunchDirectory = null, string? expectedLaunchCache = null)
    {
        launchVerificationDirectory = verifyLaunchDirectory;
        this.expectedLaunchCache = expectedLaunchCache;
        rendering = renderDirectory != null || verification;
        state = LauncherStorage.Load();
        SetLanguage(state.Language);
        state.Language = L.Code;
        installations = InstallationLocator.Discover(state.Installation, state.InstallationPaths);
        if (string.IsNullOrWhiteSpace(state.Installation)) state.Installation = installations.FirstOrDefault()?.Path ?? "";
        else state.Installation = InstallationLocator.Find(state.Installation);
        hardwareDetection = DetectMachineAsync();
        profile = state.Profiles.FirstOrDefault(p => p.Id == state.SelectedProfile) ?? state.Profiles[0];
        selection = new ModSelection(profile.CachePath);
        Text = "PZLauncher  ·  Community Edition";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = Theme.Font(); ForeColor = Theme.Text; BackColor = Theme.Background;
        footerStatus.Font = Theme.Font(10); footerDetail.Font = Theme.Font(9);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(1280, 800); MinimumSize = new Size(1080, 700);
        DoubleBuffered = true; ResizeRedraw = true;
        InitializeWindowPlacement();
        BuildShell();
        BuildWindowChrome();
        profileCombo.SelectedIndexChanged += ProfileChanged;
        languageCombo.SelectionChangeCommitted += LanguageSelectionCommitted;
        play.Click += async (_, _) => { if (page == "Online") await JoinOnlineAsync(); else LaunchGame(); };
        news = newsService.GetPinnedItems();
        ShowPage("Jouer");
        UpdateFooter();
        Enabled = false;
        Shown += async (_, _) =>
        {
            if (verification) { Enabled = true; return; }
            LauncherStorage.Log("Fenêtre affichée. Installation : " + state.Installation);
            await InitializeHardwareAsync();
            if (IsDisposed) return;
            Enabled = true; ShowPage(page); UpdateFooter();
            await Task.WhenAll(RefreshLibraryAsync(), RefreshNewsAsync());
            if (launchVerificationDirectory != null && renderDirectory == null) play.PerformClick();
            if (renderDirectory != null)
            {
                await ExportUiRenderings(renderDirectory);
                Close();
            }
        };
        FormClosing += (_, e) =>
        {
            if (rendering) { lifetime.Cancel(); return; }
            if (onlineBusy) { e.Cancel = true; closeAfterOnline = true; onlineCancellation?.Cancel(); return; }
            if (workshopBusy) { e.Cancel = true; closeAfterWorkshop = true; workshopCancellation?.Cancel(); return; }
            if (worldBusy) { e.Cancel = true; return; }
            if (serverSession is { Running: true }) { e.Cancel = true; ShowLocalizedMessage(T("server.close"), T("server.console"), false); return; }
            if (!ResolveDrafts()) { e.Cancel = true; return; }
            if (!ConfirmLeavingFtpCopy()) { e.Cancel = true; return; }
            state.SelectedProfile = profile.Id;
            state.Window = CaptureWindowPlacement();
            LauncherStorage.Save(state);
            lifetime.Cancel();
        };
        FormClosed += (_, _) =>
        {
            foreach (var image in images.Values) image.Dispose();
            game?.Dispose(); tips.Dispose(); lifetime.Dispose();
            lock (launchOutputLock) { launchVerificationOutput?.Dispose(); launchVerificationOutput = null; }
            serverSession?.Dispose();
        };
    }

    private void BuildShell()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(shell);
        var sidebar = new AtmosphericPanel(AtmosphereStyle.Sidebar) { Dock = DockStyle.Fill, Margin = Padding.Empty };
        shell.Controls.Add(sidebar, 0, 0);
        var brand = new LauncherBrand(); brand.SetBounds(18, 12, 179, 122); sidebar.Controls.Add(brand);
        var browse = Theme.Label(T("shell.space"), 8, true, Theme.Muted); browse.Name = "NavigationHeading";
        browse.SetBounds(29, 140, 160, 18); sidebar.Controls.Add(browse);
        string[] pages = ["Jouer", "Online", "Mods", "Sauvegardes", "Serveurs", "Réglages", "Diagnostic", "Actualités"];
        for (int i = 0; i < pages.Length; i++)
        {
            string name = pages[i];
            var button = new ActionButton(PageText(name)) { Navigation = true, IconCode = name switch { "Online" => 5, "Mods" => 1, "Sauvegardes" => 2, "Serveurs" => 6, "Réglages" => 3, "Diagnostic" => 7, "Actualités" => 4, _ => 0 }, AccessibleName = PageText(name) };
            button.Font = Theme.Heading(11.5f);
            button.SetBounds(15, browse.Bottom + 8 + i * 34, 184, 33);
            button.Click += (_, _) => { if (worldBusy || onlineBusy) return; if ((!settingsDirty && !serverDirty && !onlineDirty) || ResolveDrafts()) ShowPage(name); };
            navigation[name] = button; sidebar.Controls.Add(button);
        }
        var community = new Panel { Dock = DockStyle.Bottom, Height = 185, BackColor = Theme.Sidebar };
        var about = new ActionButton(T("shell.community")) { Navigation = true, Font = Theme.Font(9, true) };
        about.SetBounds(16, 0, 185, 43); about.Click += (_, _) => { if (ResolveDrafts()) ShowPage("À propos"); };
        community.Controls.Add(about);
        var fan = Theme.Label(T("shell.fan"), 8.5f, color: Theme.Muted);
        fan.SetBounds(24, 45, 180, 50); community.Controls.Add(fan);
        var languageLabel = Theme.Label(T("shell.language"), 7.5f, true, Theme.Muted);
        languageLabel.SetBounds(24, 90, 181, 20); community.Controls.Add(languageLabel);
        languageCombo.AccessibleName = T("shell.language"); StyleCombo(languageCombo);
        if (languageCombo.Items.Count == 0) languageCombo.Items.AddRange(L.Languages);
        languageCombo.DropDownWidth = Math.Min(460, Math.Max(170, L.Languages.Max(l =>
            TextRenderer.MeasureText(l.Name, languageCombo.Font).Width) + 36));
        languageCombo.SelectedItem = L.Languages.First(l => l.Code == L.Code);
        languageCombo.SetBounds(24, 114, 170, 32); community.Controls.Add(languageCombo);
        var version = Theme.Label("VERSION 0.21.0  /  WINDOWS", 7.5f, color: Theme.Muted);
        version.SetBounds(24, 155, 185, 24); community.Controls.Add(version); sidebar.Controls.Add(community);
        void ArrangeCommunity()
        {
            int S(int value) => LogicalToDeviceUnits(value);
            int width = Math.Max(S(120), community.Width - S(42));
            about.SetBounds(S(16), 0, width + S(8), S(34));
            int height = TextRenderer.MeasureText(fan.Text, fan.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            fan.SetBounds(S(24), S(39), width, height + S(2));
            languageLabel.SetBounds(S(24), fan.Bottom + S(7), width, S(18));
            languageCombo.SetBounds(S(24), languageLabel.Bottom + S(4), width, S(32));
            version.SetBounds(S(24), languageCombo.Bottom + S(7), width, S(24));
            community.Height = version.Bottom + S(4);
        }
        community.SizeChanged += (_, _) => ArrangeCommunity(); ArrangeCommunity();

        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Theme.Background };
        shell.Controls.Add(body, 1, 0);
        var top = new AtmosphericPanel(AtmosphereStyle.Masthead) { Dock = DockStyle.Top, Height = 104 };
        pageTitle.Font = Theme.Heading(24); pageTitle.SetBounds(28, 25, 610, 52); pageTitle.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        top.Controls.Add(pageTitle);
        var profileLabel = Theme.Label(T("shell.profile"), 7.5f, true, Theme.Muted);
        profileLabel.SetBounds(680, 14, 206, 18); profileLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right; top.Controls.Add(profileLabel);
        StyleCombo(profileCombo); profileCombo.SetBounds(681, 38, 260, 34); profileCombo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        top.Controls.Add(profileCombo);
        var add = new ActionButton("+") { Font = Theme.Font(15), AccessibleName = T("shell.create") };
        add.SetBounds(951, 33, 43, 39); add.Anchor = AnchorStyles.Top | AnchorStyles.Right; add.Click += (_, _) => CreateProfile(); top.Controls.Add(add);
        var cache = new ActionButton(T("cache.open")) { Name = "OpenProfileCache", Height = 23, Font = Theme.Font(8), Top = 75 };
        cache.Click += (_, _) => { Directory.CreateDirectory(profile.CachePath); OpenFolder(profile.CachePath); };
        cache.MouseEnter += (_, _) => tips.SetToolTip(cache, profile.CachePath); top.Controls.Add(cache);
        top.Resize += (_, _) =>
        {
            add.Left = top.Width - 71; profileCombo.Left = add.Left - 270;
            profileLabel.Left = profileCombo.Left; pageTitle.Width = Math.Max(200, profileCombo.Left - 60);
            cache.Left = profileCombo.Left; cache.Width = add.Right - profileCombo.Left;
        };
        ReloadProfileCombo();
        var bottom = new AtmosphericPanel(AtmosphereStyle.Footer) { Dock = DockStyle.Bottom, Height = 104 };
        footerStatus.SetBounds(29, 25, 620, 26); footerDetail.SetBounds(29, 54, 620, 30);
        bottom.Controls.Add(footerStatus); bottom.Controls.Add(footerDetail);
        play.Size = new Size(214, 56); play.Font = Theme.Heading(18); play.Top = 24; play.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        bottom.Controls.Add(play);
        void ArrangeFooter()
        {
            int textWidth = TextRenderer.MeasureText(play.Text, play.Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            play.Width = Math.Clamp(textWidth + 70, 214, Math.Max(214, Math.Min(360, bottom.Width - 300)));
            play.Left = bottom.Width - play.Width - 28;
            footerStatus.Width = footerDetail.Width = Math.Max(180, play.Left - 55);
        }
        bottom.Resize += (_, _) => ArrangeFooter();
        EventHandler playTextChanged = (_, _) => ArrangeFooter();
        play.TextChanged += playTextChanged;
        bottom.Disposed += (_, _) => play.TextChanged -= playTextChanged;
        body.Controls.Add(pageHost); body.Controls.Add(top); body.Controls.Add(bottom);
    }

    private static void StyleCombo(ComboBox combo)
    {
        combo.Font = Theme.Font(10);
        if (combo.DrawMode == DrawMode.OwnerDrawFixed)
        {
            combo.ItemHeight = Math.Max(25, combo.Font.Height + 5);
            return;
        }
        combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Theme.Surface; combo.ForeColor = Theme.Text;
        combo.IntegralHeight = false; combo.DropDownHeight = 240;
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        
        
        combo.ItemHeight = Math.Max(25, combo.Font.Height + 5);
        combo.DrawItem += (_, e) =>
        {
            using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Hover : Theme.Surface);
            e.Graphics.FillRectangle(background, e.Bounds);
            string text = e.Index >= 0 && e.Index < combo.Items.Count ? combo.GetItemText(combo.Items[e.Index]) ?? "" : combo.Text;
            TextRenderer.DrawText(e.Graphics, text, combo.Font, Rectangle.Inflate(e.Bounds, -9, 0), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        };
    }
    private void ReloadProfileCombo()
    {
        switchingProfile = true; profileCombo.Items.Clear();
        foreach (var p in state.Profiles) profileCombo.Items.Add(p);
        profileCombo.SelectedItem = profile; switchingProfile = false;
    }
    private void ShowPage(string name)
    {
        if (workshopBusy || onlineBusy) return;
        page = name; pageHost.SuspendLayout();
        foreach (Control control in pageHost.Controls.Cast<Control>().ToArray()) control.Dispose();
        hero = null; collectionStatus = null; savesStatus = null; modGrid = null; savesGrid = null;
        foreach (var item in navigation) { item.Value.Active = item.Key == name; item.Value.Invalidate(); }
        pageTitle.Text = name switch { "Jouer" => T("title.home"), "Online" => T("online.title"), "Mods" => T("title.mods"), "Sauvegardes" => T("title.saves"), "Serveurs" => T("server.title"), "Diagnostic" => T("log.title"), "Réglages" => T("title.settings"), "Actualités" => T("title.news"), _ => T("title.about") };
        pageTitle.Text = pageTitle.Text.ToUpper(L.Culture);
        switch (name)
        {
            case "Jouer": BuildHome(); break;
            case "Online": BuildOnline(); break;
            case "Mods": BuildMods(); break;
            case "Sauvegardes": BuildSaves(); break;
            case "Serveurs": BuildServers(); break;
            case "Diagnostic": BuildDiagnostics(); break;
            case "Réglages": BuildSettings(); break;
            case "Actualités": BuildNews(); break;
            default: BuildAbout(); break;
        }
        pageHost.ResumeLayout(true);
        UpdateFooter();
    }

    private void BuildHome()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        pageHost.Controls.Add(layout);
        hero = new HeroCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 18, 18), Item = news.FirstOrDefault() };
        if (hero.Item != null && images.TryGetValue(hero.Item.ImageUrl, out var cover)) hero.CoverImage = cover;
        hero.Click += (_, _) => { if (hero?.Item is NewsItem article) OpenNews(article); else OpenUrl("https://projectzomboid.com/blog/news/"); };
        layout.Controls.Add(hero, 0, 0);
        var setup = new SurfacePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 18) }; layout.Controls.Add(setup, 1, 0);
        var tag = Theme.Label(T("home.session"), 8, true, Theme.AccentText); tag.SetBounds(22, 22, 245, 22); setup.Controls.Add(tag);
        var title = Theme.Label(profile.Name, 19, true); title.SetBounds(22, 55, 245, 43); title.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right; setup.Controls.Add(title);
        bool gog = InstallationLocator.IsGog(state.Installation);
        var steam = Check(T("steam.use"), profile.Steam && !gog); steam.SetBounds(22, 114, 245, 27); steam.Enabled = profile.OnlineServerId.Length == 0 && !gog;
        steam.CheckedChanged += async (_, _) => { profile.Steam = steam.Checked; LauncherStorage.Save(state); UpdateFooter(); await RefreshLibraryAsync(); };
        tips.SetToolTip(steam, T(gog ? "install.gogMode" : profile.OnlineServerId.Length == 0 ? "steam.tip" : "online.editConnection")); setup.Controls.Add(steam);
        var voice = Check(T("voice.short"), profile.Voice); voice.SetBounds(22, 154, 245, 27);
        voice.CheckedChanged += (_, _) => { profile.Voice = voice.Checked; LauncherStorage.Save(state); }; setup.Controls.Add(voice);
        var memory = Theme.Label(profile.MemoryMb == 0 ? T("memory.default") : T("memory.value", profile.MemoryMb / 1024.0), 9, color: Theme.Muted);
        memory.SetBounds(22, 197, 245, 28); setup.Controls.Add(memory);
        var settings = new ActionButton(T("home.customize")); settings.SetBounds(22, 241, 210, 39);
        setup.Controls.Add(settings);
        setup.Layout += (_, _) =>
        {
            foreach (Control c in setup.Controls) c.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            title.Width = tag.Width = steam.Width = voice.Width = memory.Width = settings.Width = Math.Max(100, setup.Width - 44);
            steam.Top = 103; voice.Top = 139; memory.Top = 176;
            settings.SetBounds(22, setup.Height - 58, setup.Width - 44, 37);
            memory.Visible = memory.Bottom + 6 < settings.Top;
        };
        settings.Click += (_, _) => ShowPage("Réglages");

        var publications = new SurfacePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 18, 0) }; layout.Controls.Add(publications, 0, 1);
        var section = Theme.Label(T("home.feed"), 8, true, Theme.AccentText); section.SetBounds(22, 20, 220, 20); publications.Controls.Add(section);
        int y = 54;
        var publicationRows = new List<(Control title, Control date)>();
        foreach (var item in news.Skip(1).Take(2))
        {
            var titleButton = new ActionButton(item.Title) { Font = Theme.Font(10, true), TextAlign = ContentAlignment.MiddleLeft };
            titleButton.SetBounds(20, y, publications.Width - 40, 43); titleButton.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            titleButton.Click += (_, _) => OpenNews(item); publications.Controls.Add(titleButton);
            var date = Theme.Label(item.Published.ToLocalTime().ToString("dd MMM yyyy", L.Culture) + "  ·  The Indie Stone", 8, color: Theme.Muted);
            date.SetBounds(25, y + 46, 430, 24); date.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; publications.Controls.Add(date); y += 80;
            publicationRows.Add((titleButton, date));
        }
        if (news.Count == 0)
        {
            var empty = Theme.Label(T("home.feedEmpty"), 12, color: Theme.Muted);
            empty.SetBounds(24, 60, 500, 80); publications.Controls.Add(empty);
        }
        var allNews = new ActionButton(T("home.allNews")) { Font = Theme.Font(9) };
        allNews.SetBounds(22, 12, 120, 31);
        allNews.Click += (_, _) => ShowPage("Actualités"); publications.Controls.Add(allNews);
        publications.Layout += (_, _) =>
        {
            allNews.Left = publications.Width - 142;
            int rowHeight = Math.Max(62, (publications.Height - 59) / 2);
            for (int index = 0; index < publicationRows.Count; index++)
            {
                var row = publicationRows[index]; row.title.Anchor = row.date.Anchor = AnchorStyles.Left | AnchorStyles.Top;
                row.title.SetBounds(20, 53 + rowHeight * index, publications.Width - 40, 37);
                row.date.SetBounds(25, 94 + rowHeight * index, publications.Width - 50, 22);
            }
        };

        var library = new SurfacePanel { Dock = DockStyle.Fill, Margin = Padding.Empty }; layout.Controls.Add(library, 1, 1);
        var shortcuts = Theme.Label(T("home.shortcuts"), 8, true, Theme.AccentText); shortcuts.SetBounds(22, 20, 240, 22); library.Controls.Add(shortcuts);
        collectionStatus = Theme.Label(T("home.modsCount", mods.Count), 15, true); collectionStatus.SetBounds(22, 55, 245, 32); library.Controls.Add(collectionStatus);
        var toMods = new ActionButton(T("home.toMods")); toMods.SetBounds(22, 93, 245, 38); toMods.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        toMods.Click += (_, _) => ShowPage("Mods"); library.Controls.Add(toMods);
        savesStatus = Theme.Label(T("home.savesCount", saves.Count), 15, true); savesStatus.SetBounds(22, 150, 245, 32); library.Controls.Add(savesStatus);
        var toSaves = new ActionButton(T("home.toSaves")); toSaves.SetBounds(22, 188, 245, 38); toSaves.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        toSaves.Click += (_, _) => ShowPage("Sauvegardes"); library.Controls.Add(toSaves);
        library.Layout += (_, _) =>
        {
            foreach (Control c in library.Controls) c.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            int second = 50 + (library.Height - 54) / 2;
            collectionStatus.SetBounds(22, 50, library.Width - 44, 28);
            toMods.SetBounds(22, 81, library.Width - 44, 32);
            savesStatus.SetBounds(22, second, library.Width - 44, 28);
            toSaves.SetBounds(22, second + 31, library.Width - 44, 32);
        };
    }

    private async Task RefreshLibraryAsync()
    {
        int generation = ++refreshGeneration; var selected = profile;
        libraryReady = false; UpdateFooter();
        try
        {
            var data = await Task.Run(() =>
            {
                var issues = new List<string>();
                return (mods: ModCatalog.Scan(selected, InstallationLocator.ReadGameVersion(state.Installation), issues), saves: SaveCatalog.Scan(selected.CachePath), issues);
            });
            if (IsDisposed || lifetime.IsCancellationRequested || generation != refreshGeneration) return;
            mods = data.mods; saves = data.saves; modScanIssues = data.issues;
            libraryReady = true;
            foreach (string missing in selection.Ids.Where(id => mods.All(m => m.Id != id)))
                mods.Add(new() { Id = missing, Name = missing, Source = "Introuvable", Target = "À vérifier" });
            if (collectionStatus != null) collectionStatus.Text = T("home.modsCount", mods.Count);
            if (savesStatus != null) savesStatus.Text = T("home.savesCount", saves.Count);
            if (page == "Mods") FillMods();
            if (page == "Sauvegardes") FillSaves();
            UpdateFooter();
            LauncherStorage.Log($"Bibliothèque chargée : {mods.Count} mods, {saves.Count} sauvegardes.");
        }
        catch (Exception ex) { if (!IsDisposed) ShowError(ex); }
    }
    private async Task RefreshNewsAsync()
    {
        var result = await newsService.RefreshPinnedItemsAsync(lifetime.Token);
        if (IsDisposed || lifetime.IsCancellationRequested) return;
        news = result;
        if (page is "Jouer" or "Actualités") ShowPage(page);
        foreach (var item in news.Take(4))
        {
            if (images.ContainsKey(item.ImageUrl)) continue;
            Image? image = await newsService.GetImageAsync(item, lifetime.Token);
            if (image == null) continue;
            if (IsDisposed || lifetime.IsCancellationRequested) { image.Dispose(); return; }
            images[item.ImageUrl] = image;
            if (hero?.Item?.ImageUrl == item.ImageUrl) { hero.CoverImage = image; hero.Invalidate(); }
        }
        LauncherStorage.Log($"Actualités : {news.Count} articles, cache={newsService.FromCache}.");
    }

    private void UpdateFooter()
    {
        bool valid = InstallationLocator.IsValid(state.Installation);
        bool running = game is { HasExited: false };
        play.Enabled = !installationBusy && !worldBusy && !preparingLaunch && !workshopBusy && !onlineBusy && !running && (!valid || libraryReady);
        play.Text = running ? T("status.running") : !valid ? T("status.locate") : libraryReady ? T(page == "Online" || profile.OnlineServerId.Length > 0 || profile.DedicatedServerId.Length > 0 ? "online.join" : "status.play") : T("status.preparing");
        if (preparingLaunch) play.Text = T("java.preparing");
        play.Invalidate();
        footerStatus.Text = running ? T("status.gameRunning") : valid ? T("status.ready") : T("status.find");
        footerDetail.Text = T("status.detail", profile.Name, profile.Steam && !InstallationLocator.IsGog(state.Installation) ? T("steam.on") : T("steam.off"), selection.Ids.Count);
        if (page == "Online" && onlineDraft != null) footerDetail.Text = T("online.profileSummary", onlineDraft.Name);
        tips.SetToolTip(footerStatus, state.Installation);
    }
    internal void VerifyLaunchCacheGuard() => LaunchGame();
    private async void LaunchGame()
    {
        if (worldBusy || preparingLaunch || workshopBusy || onlineBusy) return;
        if (!ResolveDrafts()) return;
        if (!InstallationLocator.IsValid(state.Installation)) { PickInstallation(); return; }
        if (game is { HasExited: false }) return;
        try
        {
            if (expectedLaunchCache != null && !RuntimeClients.SameCache(expectedLaunchCache, profile.CachePath)) throw new InvalidDataException("Verification aborted: unexpected client cache.");
            
            if (!ArrangeMods() || !SaveModChanges()) return;
            preparingLaunch = true; UpdateFooter(); pageHost.Enabled = profileCombo.Enabled = false;
            ValidateOnlineLaunch();
            if (profile.DedicatedServerId.Length > 0)
            {
                var server = await Task.Run(() => RuntimeClients.VerifyBinding(state, profile, state.Installation)); RuntimeClients.SetConnection(profile, server);
            }
            var unavailable = mods.Where(m => selection.Ids.Contains(m.Id) && !m.Available).ToList();
            if (unavailable.Count > 0)
                throw new InvalidOperationException(T("error.unavailable", string.Join("\n", unavailable.Take(8).Select(m => m.Name))));
            if (profile.AutomaticJvm)
            {
                hardware = await DetectMachineAsync();
                if (IsDisposed) return;
                if (hardware.TotalMb == 0) throw new InvalidOperationException(T("jvm.detectFailed"));
                JvmArguments.ApplySuggestion(profile, SuggestedJvm);
            }
            var plan = launchService.CreateLaunchPlan(state.Installation, profile.AsGameProfile(), profile.AsJvmProfile());
            plan = await Task.Run(() => RuntimeClients.Attach(plan, profile, state.Installation));
            if (profile.JavaLoaderEnabled)
            {
                preparingLaunch = true; UpdateFooter();
                
                pageHost.Enabled = profileCombo.Enabled = false;
                var catalog = JavaMods.Scan(profile, mods, selection.Ids, state.Installation);
                var java = JavaMods.Prepare(profile, catalog, state.Installation);
                RuntimeClients.CheckOverlayConflicts(plan.Arguments[plan.Arguments.ToList().IndexOf("-cp") + 1].Split(';').Where(Path.IsPathRooted), java);
                await JavaMods.PreflightAsync(plan, java);
                if (IsDisposed) return;
                plan = JavaMods.Attach(plan, java);
            }
            LauncherStorage.Save(state);
            LauncherStorage.Log("Démarrage demandé : " + plan.CommandLinePreview);
            if (launchVerificationDirectory != null)
            {
                Directory.CreateDirectory(launchVerificationDirectory);
                lock (launchOutputLock)
                {
                    launchVerificationOutput?.Dispose();
                    launchVerificationOutput = new StreamWriter(Path.Combine(launchVerificationDirectory, "launcher-game.log")) { AutoFlush = true };
                }
            }
            void Output(string? line)
            {
                if (line == null) return;
                lock (launchOutputLock) launchVerificationOutput?.WriteLine(line);
                if (!line.Contains("[class,load]")) LauncherStorage.Log(line);
            }
            game?.Dispose();
            game = launchService.Start(plan, (_, e) => Output(e.Data),
                (_, e) => Output(e.Data),
                (_, _) => { if (!IsDisposed && IsHandleCreated) BeginInvoke(() => { UpdateFooter(); _ = RefreshLibraryAsync(); }); });
            if (launchVerificationDirectory != null)
                File.WriteAllText(Path.Combine(launchVerificationDirectory, "launch.json"), System.Text.Json.JsonSerializer.Serialize(new
                { processId = game.Id, profile.Name, profile.Id, profile.AutomaticJvm, plan, hardware, suggestion = SuggestedJvm }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            UpdateFooter();
        }
        catch (Exception ex)
        {
            if (launchVerificationDirectory != null)
            {
                Directory.CreateDirectory(launchVerificationDirectory); File.WriteAllText(Path.Combine(launchVerificationDirectory, "error.txt"), ex.ToString());
            }
            else if (!IsDisposed) ShowError(ex);
        }
        finally { preparingLaunch = false; if (!IsDisposed) { pageHost.Enabled = profileCombo.Enabled = true; UpdateFooter(); } }
    }
    private void CreateProfile()
    {
        if (preparingLaunch) return;
        if (!ResolveDrafts()) return;
        using var dialog = BuildProfileDialog(out var input);
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(input.Text)) return;
        try
        {
            var next = System.Text.Json.JsonSerializer.Deserialize<PlayerProfile>(System.Text.Json.JsonSerializer.Serialize<ProfilePreferences>(profile))!;
            next.CopyLocalJvmFrom(profile);
            next.Name = input.Text.Trim();
            if (profile.OnlineServerId.Length > 0) { next.Launch.ConnectAddress = ""; next.Launch.ConnectPassword = ""; }
            next.CachePath = Path.Combine(LauncherStorage.Root, "profiles", next.Id, "cache");
            Directory.CreateDirectory(next.CachePath);
            string currentOptions = Path.Combine(profile.CachePath, "options.ini");
            if (File.Exists(currentOptions)) File.Copy(currentOptions, Path.Combine(next.CachePath, "options.ini"));
            state.Profiles.Add(next); profile = next; state.SelectedProfile = next.Id;
            selection = new(next.CachePath); LauncherStorage.Save(state); ReloadProfileCombo(); ShowPage(page); UpdateFooter(); _ = RefreshLibraryAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private Form BuildProfileDialog(out TextBox input)
    {
        var dialog = new Form { Text = T("profile.new"), ClientSize = new Size(480, 247), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Background, ForeColor = Theme.Text, Font = Theme.Font() };
        var label = Theme.Label(T("profile.heading"), 17, true); label.SetBounds(24, 20, 390, 40); dialog.Controls.Add(label);
        input = new TextBox { PlaceholderText = T("profile.name"), AccessibleName = T("profile.name"), BackColor = Theme.Surface, ForeColor = Theme.Text, MaxLength = 64 };
        input.SetBounds(24, 76, 432, 30); dialog.Controls.Add(input);
        var hint = Theme.Label(T("profile.hint"), 9, color: Theme.Muted);
        hint.SetBounds(24, 116, 432, 50); dialog.Controls.Add(hint);
        var create = new ActionButton(T("profile.create")) { Primary = true, DialogResult = DialogResult.OK }; create.SetBounds(246, 181, 210, 42); dialog.Controls.Add(create); dialog.AcceptButton = create;
        var cancel = new ActionButton(T("dialog.cancel")) { DialogResult = DialogResult.Cancel };
        cancel.SetBounds(24, 181, 160, 42); dialog.Controls.Add(cancel); dialog.CancelButton = cancel;
        return dialog;
    }
    private static CheckBox Check(string text, bool value) => new StyledCheckBox()
    { Text = text, Checked = value, ForeColor = Theme.Text, BackColor = Theme.Surface, Font = Theme.Font(10), AutoSize = false };
    private void OpenNews(NewsItem item) { if (NewsService.IsOfficialUrl(item.Url)) OpenUrl(item.Url); }
    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) { ShowError(new DirectoryNotFoundException(T("folder.missing"))); return; }
        OpenUrl(path);
    }
    private void ShowError(Exception ex)
    {
        LauncherStorage.Log(ex.ToString()); ShowLocalizedMessage(ex.Message, "PZLauncher", false);
    }
    private bool ResolveDrafts()
    {
        if (worldBusy || workshopBusy || onlineBusy) return false;
        if (!modsDirty && !settingsDirty && !serverDirty && !onlineDirty) return true;
        var answer = ShowLocalizedMessage(T("draft.question"), T("draft.title"), true);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes && (!SaveModChanges() || !SaveSettings())) return false;
        if (answer == DialogResult.Yes && serverDirty && saveServerDraft?.Invoke() != true) return false;
        if (answer == DialogResult.Yes && onlineDirty && !SaveOnlineFavorite()) return false;
        if (answer == DialogResult.No) { selection = new ModSelection(profile.CachePath); modsDirty = false; settingsDirty = false; serverDirty = false; onlineDirty = false; onlineDraft = null; }
        return true;
    }
}
