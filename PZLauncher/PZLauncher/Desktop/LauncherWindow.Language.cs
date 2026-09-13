namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private bool languageChangePending;
    private Exception? languageChangeError;
    private void LanguageSelectionCommitted(object? sender, EventArgs e)
    {
        if (languageCombo.SelectedItem is L.Language selected) QueueLanguageChange(selected.Code, !rendering);
    }
    private void QueueLanguageChange(string code, bool persist)
    {
        if (languageChangePending || code == L.Code || IsDisposed || Disposing) return;
        languageChangePending = true;
        
        BeginInvoke((Action)(() =>
        {
            try
            {
                if (IsDisposed || Disposing) return;
                languageCombo.DroppedDown = false;
                if (worldBusy || !ResolveDrafts())
                {
                    languageCombo.SelectedItem = L.Languages.First(l => l.Code == L.Code);
                    return;
                }
                ApplyLanguage(code, persist);
            }
            catch (Exception ex)
            {
                languageChangeError = ex; LauncherStorage.Log(ex.ToString());
                if (!rendering && !IsDisposed) ShowError(ex);
            }
            finally { languageChangePending = false; }
        }));
    }
    private static string PageText(string name) => T(name switch
    {
        "Jouer" => "nav.play", "Online" => "online.nav", "Mods" => "nav.mods", "Sauvegardes" => "nav.saves",
        "Réglages" => "nav.settings", "Actualités" => "nav.news", "Serveurs" => "server.nav", "Diagnostic" => "log.nav", _ => "nav.about"
    });
    private static string ModLabel(string value) => value switch
    {
        "Local" => T("mods.local"), "Introuvable" => T("mods.missing"),
        "Staged" => T("mods.staged"),
        "Commun B42" => T("mods.common"), "Ancien format" => T("mods.legacy"),
        "À vérifier" => T("mods.check"), _ => value
    };
    private async void ProfileChanged(object? sender, EventArgs e)
    {
        if (switchingProfile || profileCombo.SelectedItem is not PlayerProfile next || next.Id == profile.Id) return;
        if (!ResolveDrafts()) { ReloadProfileCombo(); return; }
        profile = next; state.SelectedProfile = profile.Id; LauncherStorage.Save(state);
        selectedWorld = ""; diagnosticPath = "";
        selection = new ModSelection(profile.CachePath); modsDirty = false;
        mods = []; saves = []; ShowPage(page); UpdateFooter(); await RefreshLibraryAsync();
    }
    private void ApplyLanguage(string code, bool persist)
    {
        string search = modSearch?.IsDisposed == false ? modSearch.Text : "";
        int filter = modFilter?.IsDisposed == false ? modFilter.SelectedIndex : 0;
        int selectedTab = Descendants(pageHost).OfType<ThemedTabs>().FirstOrDefault()?.SelectedIndex ?? 0;
        SetLanguage(code);
        if (persist) { state.Language = L.Code; LauncherStorage.Save(state); }
        SuspendLayout();
        Font = Theme.Font();
        footerStatus.Font = Theme.Font(10);
        footerDetail.Font = Theme.Font(9);
        
        foreach (Control control in new Control[] { pageHost, profileCombo, languageCombo, pageTitle, footerStatus, footerDetail, play })
            control.Parent?.Controls.Remove(control);
        foreach (Control control in Controls.Cast<Control>().ToArray()) control.Dispose();
        navigation.Clear();
        BuildShell(); BuildWindowChrome(); ShowPage(page); UpdateFooter();
        ResumeLayout(true);
        if (page == "Mods")
        {
            if (modSearch != null) modSearch.Text = search;
            if (modFilter != null) modFilter.SelectedIndex = Math.Max(0, filter);
        }
        if (Descendants(pageHost).OfType<ThemedTabs>().FirstOrDefault() is { } tabs)
            tabs.SelectedIndex = Math.Min(selectedTab, tabs.TabCount - 1);
    }
    private DialogResult ShowLocalizedMessage(string message, string title, bool question)
    {
        using var dialog = CreateMessageDialog(message, title, question);
        return dialog.ShowDialog(this);
    }
    internal void VerifyLanguageBehavior(string fixtureRoot)
    {
        profile = new PlayerProfile { Name = "Test profile", CachePath = fixtureRoot, Collector = "G1" };
        state.Profiles = [profile];
        selection = new ModSelection(fixtureRoot);
        selection.Ids.Add("fixture-local");
        mods =
        [
            new InstalledMod { Id = "fixture-local", Name = "Local fixture", Source = "Local", Available = true, Target = "Commun B42" },
            new InstalledMod { Id = "fixture-workshop", Name = "Workshop fixture", Source = "Workshop", Available = true, Target = "Commun B42" }
        ];
        foreach (var language in L.Languages)
        {
            ShowPage("Mods");
            modFilter!.SelectedIndex = 2;
            modSearch!.Text = "fixture";
            ApplyLanguage(language.Code, false);
            if (modFilter!.SelectedIndex != 2 || modSearch!.Text != "fixture" || modGrid!.Rows.Count != 1 ||
                ((InstalledMod)modGrid.Rows[0].Tag!).Id != "fixture-workshop")
                throw new InvalidOperationException("A translated filter changed its meaning: " + language.Code);
            modFilter.SelectedIndex = 3;
            if (modGrid.Rows.Count != 1 || ((InstalledMod)modGrid.Rows[0].Tag!).Id != "fixture-local")
                throw new InvalidOperationException("The local filter changed its meaning: " + language.Code);
            ShowPage("Réglages");
            if (collectorEditor!.SelectedIndex != 0 || !profile.AsJvmProfile().UseOptimizedG1 ||
                !selection.Ids.SequenceEqual(new[] { "fixture-local" }) || settingsDirty || modsDirty)
                throw new InvalidOperationException("Changing language modified game settings or selection.");
        }
    }
    internal void VerifyLanguageNotifications(string fixtureRoot)
    {
        profile = new PlayerProfile { Name = "Language fixture", CachePath = fixtureRoot };
        state.Profiles = [profile]; selection = new ModSelection(fixtureRoot);
        selection.Ids.Add("fixture-mod");
        Opacity = 0; ShowInTaskbar = false; Show();
        ApplyLanguage("en", false);
        var originalCombo = languageCombo;
        void VerifyComboRendering(ComboBox combo, string name, bool save)
        {
            int drawCalls = 0;
            Rectangle itemBounds = Rectangle.Empty;
            DrawItemEventHandler observed = (_, e) => { drawCalls++; itemBounds = e.Bounds; };
            using var bitmap = new Bitmap(combo.Width, combo.Height);
            combo.DrawItem += observed;
            try { combo.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); }
            finally { combo.DrawItem -= observed; }
            if (drawCalls == 0 || combo.SelectedIndex < 0 || string.IsNullOrWhiteSpace(combo.Text))
                throw new InvalidOperationException($"Native selector no longer draws its selection: {L.Code}/{name}.");
            
            Rectangle content = Rectangle.Intersect(Rectangle.Inflate(itemBounds, -9, 0), new Rectangle(Point.Empty, bitmap.Size));
            int textPixels = 0;
            for (int y = content.Top; y < content.Bottom; y++)
                for (int x = content.Left; x < content.Right; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (pixel.R >= Theme.Text.R - 45 && pixel.G >= Theme.Text.G - 45 && pixel.B >= Theme.Text.B - 45) textPixels++;
                }
            if (textPixels < 5)
                throw new InvalidOperationException($"Native selector renders no text pixels: {L.Code}/{name}.");
            if (save)
            {
                string directory = Path.Combine(fixtureRoot, "language-selector-render");
                Directory.CreateDirectory(directory);
                bitmap.Save(Path.Combine(directory, L.Code + "-" + name + ".png"));
            }
        }
        VerifyComboRendering(languageCombo, "language", true);
        VerifyComboRendering(profileCombo, "profile", true);
        for (int cycle = 0; cycle < 3; cycle++)
            foreach (string target in L.Languages.Where(l => l.Code != "en").SelectMany(l => new[] { l.Code, "en" }))
            {
                string previous = L.Code;
                languageCombo.SelectedItem = L.Languages.First(l => l.Code == target);
                LanguageSelectionCommitted(languageCombo, EventArgs.Empty);
                if (L.Code != previous || languageCombo.IsDisposed)
                    throw new InvalidOperationException("The selector rebuilt its shell inside the selection notification.");
                Application.DoEvents();
                if (languageChangeError != null) throw new InvalidOperationException("Queued language change failed.", languageChangeError);
                if (L.Code != target || languageChangePending || languageCombo.IsDisposed || !languageCombo.IsHandleCreated
                    || !ReferenceEquals(originalCombo, languageCombo) || navigation.Count != 8
                    || !selection.Ids.SequenceEqual(new[] { "fixture-mod" }))
                    throw new InvalidOperationException("Language round trip did not preserve the live selector or profile: " + target);
                VerifyComboRendering(languageCombo, "language", cycle == 0);
                VerifyComboRendering(profileCombo, "profile", cycle == 0);
            }
        Close();
    }
    
    private Form CreateMessageDialog(string message, string title, bool question)
    {
        var dialog = new Form
        {
            Text = title, ClientSize = new Size(590, 230), MinimumSize = new Size(590, 250),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.Sizable,
            MaximizeBox = false, MinimizeBox = false, BackColor = Theme.Background,
            ForeColor = Theme.Text, Font = Theme.Font(), AutoScaleMode = AutoScaleMode.Dpi
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(16), FlowDirection = FlowDirection.RightToLeft };
        void Add(string text, DialogResult result, bool primary)
        {
            var button = new ActionButton(text) { DialogResult = result, Primary = primary, Width = 165, Height = 40, Margin = new Padding(4, 0, 0, 0) };
            actions.Controls.Add(button);
            if (result is DialogResult.Cancel or DialogResult.OK) dialog.CancelButton = button;
            if (primary) dialog.AcceptButton = button;
        }
        if (question) { Add(T("dialog.cancel"), DialogResult.Cancel, false); Add(T("dialog.discard"), DialogResult.No, false); Add(T("dialog.save"), DialogResult.Yes, true); }
        else Add(T("dialog.ok"), DialogResult.OK, true);
        var copy = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.Background, ForeColor = Theme.Text, Font = Theme.Font(11), Text = message };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24) };
        body.Controls.Add(copy); dialog.Controls.Add(body); dialog.Controls.Add(actions);
        return dialog;
    }
}
