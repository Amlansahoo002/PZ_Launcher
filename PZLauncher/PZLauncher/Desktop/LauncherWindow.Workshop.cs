namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private bool workshopBusy;
    private bool closeAfterWorkshop;
    private CancellationTokenSource? workshopCancellation;
    private SteamCmdClient? workshopClient;
    private string workshopDraft = "", workshopAccount = "", workshopProfileId = "";
    private string workshopSummary = "";

    private void BuildWorkshop(Control parent, ThemedTabs tabs)
    {
        if (workshopProfileId != profile.Id)
        {
            workshopProfileId = profile.Id; workshopDraft = ""; workshopSummary = "";
        }
        var top = new Panel { Dock = DockStyle.Top, Height = 236 };
        var note = Theme.Label(T("workshop.note"), 9, color: Theme.Muted);
        top.Controls.Add(note);
        var idsLabel = Theme.Label(T("workshop.inputLabel"), 9);
        top.Controls.Add(idsLabel);
        var ids = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Text = workshopDraft,
            PlaceholderText = T("workshop.ids"), AccessibleName = T("workshop.ids"), BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        top.Controls.Add(ids); ids.TextChanged += (_, _) => workshopDraft = ids.Text;
        var accountLabel = Theme.Label(T("workshop.account"), 9);
        var username = new TextBox { Text = workshopAccount, PlaceholderText = T("workshop.anonymous"), AccessibleName = T("workshop.account"),
            BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, MaxLength = 64 };
        username.TextChanged += (_, _) => workshopAccount = username.Text;
        tips.SetToolTip(username, T("workshop.anonymous"));
        var login = new ActionButton(T("workshop.login")) { Height = 34 };
        tips.SetToolTip(login, T("workshop.nativeLogin"));
        top.Controls.AddRange([accountLabel, username, login]);
        var validate = Check(T("workshop.validate"), true); tips.SetToolTip(validate, T("workshop.validateTip")); top.Controls.Add(validate);
        var actions = new FlowLayoutPanel { Height = 44, WrapContents = false, Margin = Padding.Empty };
        var download = new ActionButton(T("workshop.download")) { Primary = true, Width = 225, Height = 40 };
        var selected = new ActionButton(T("workshop.fromSelection")) { Width = 150, Height = 40 };
        var update = new ActionButton(T("workshop.updateAll")) { Width = 190, Height = 40 };
        var cancel = new ActionButton(T("dialog.cancel")) { Width = 110, Height = 40, Enabled = false };
        actions.Controls.AddRange([download, selected, update, cancel]); top.Controls.Add(actions);
        var status = Theme.Label("", 9, color: Theme.Muted); top.Controls.Add(status);
        var output = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = new Font("Consolas", 9), BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = T("workshop.log") };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var target = Theme.Label(T("workshop.destination", profile.Name), 8.5f, color: Theme.Muted);
        var folder = new ActionButton(T("workshop.files")) { Dock = DockStyle.Right, Width = 160, Height = 36 };
        var logFolder = new ActionButton(T("workshop.logs")) { Dock = DockStyle.Right, Width = 160, Height = 36 };
        logFolder.Width = Math.Max(logFolder.Width, TextRenderer.MeasureText(logFolder.Text, logFolder.Font,
            Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + 28);
        bottom.Controls.Add(target); bottom.Controls.Add(logFolder); bottom.Controls.Add(folder);
        tips.SetToolTip(target, Path.Combine(profile.CachePath, "mods"));
        parent.Controls.Add(output); parent.Controls.Add(top); parent.Controls.Add(bottom);
        void Arrange()
        {
            int width = top.ClientSize.Width;
            note.SetBounds(0, 0, width, 38);
            idsLabel.SetBounds(0, 39, width, 20);
            ids.SetBounds(0, 60, width, 42);
            accountLabel.SetBounds(0, 114, 125, 24);
            username.SetBounds(130, 110, Math.Max(180, width - 520), 29);
            login.SetBounds(username.Right + 8, 107, 185, 40);
            validate.SetBounds(login.Right + 8, 109, Math.Max(120, width - login.Right - 8), 32);
            actions.SetBounds(0, 150, width, 44); status.SetBounds(0, 199, width, 30);
            target.SetBounds(0, 7, Math.Max(180, bottom.Width - folder.Width - logFolder.Width - 14), 31);
        }
        top.Resize += (_, _) => Arrange(); bottom.Resize += (_, _) => Arrange();
        void Refresh()
        {
            if (output.IsDisposed) return;
            string log = workshopClient?.Output ?? T("workshop.idleHelp").Replace("\n", Environment.NewLine);
            if (output.Text != log) { output.Text = log; output.SelectionStart = output.TextLength; output.ScrollToCaret(); }
            status.Text = workshopSummary.Length > 0 ? workshopSummary : T("workshop.tracked", WorkshopStore.Read(profile.CachePath).Count);
        }
        var timer = new System.Windows.Forms.Timer { Interval = 300 };
        timer.Tick += (_, _) => { if (workshopBusy) Refresh(); };
        parent.Disposed += (_, _) => timer.Dispose(); timer.Start();
        void Busy(bool busy)
        {
            workshopBusy = busy; tabs.SelectionLocked = busy;
            profileCombo.Enabled = languageCombo.Enabled = !busy;
            foreach (var button in navigation.Values) button.Enabled = !busy;
            ids.Enabled = username.Enabled = login.Enabled = validate.Enabled = download.Enabled = selected.Enabled = update.Enabled = !busy;
            cancel.Enabled = busy; UpdateFooter();
        }
        async Task Run(bool authentication, IReadOnlyList<string> itemIds)
        {
            if (workshopBusy || !ResolveDrafts()) return;
            try
            {
                GameProcessGuard.EnsureStopped(profile.CachePath);
                string account = SteamCmdClient.Account(username.Text);
                if (authentication && account == "anonymous") throw new InvalidDataException(T("workshop.accountRequired"));
                var targetProfile = profile;
                workshopClient = new SteamCmdClient();
                workshopCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                workshopSummary = T(authentication ? "workshop.authenticating" : "workshop.running");
                Busy(true); Refresh();
                if (authentication)
                {
                    await workshopClient.LoginAsync(account, workshopCancellation.Token);
                    workshopSummary = T("workshop.loginClosed");
                }
                else
                {
                    var result = await workshopClient.DownloadAsync(targetProfile.CachePath, itemIds, account, validate.Checked, workshopCancellation.Token);
                    int passed = result.Count(r => r.Success);
                    if (passed > 0)
                    {
                        targetProfile.Launch.ModFolders = WorkshopStore.PreferLocal(targetProfile.Launch.ModFolders);
                        LauncherStorage.Save(state);
                    }
                    workshopSummary = T("workshop.finished", passed, result.Count - passed);
                }
            }
            catch (OperationCanceledException) { workshopSummary = T("workshop.cancelled"); }
            catch (Exception ex)
            {
                workshopSummary = T("workshop.failed"); workshopClient?.Log("\n" + ex.Message + "\n");
                if (!IsDisposed) ShowError(ex);
            }
            finally
            {
                workshopCancellation?.Dispose(); workshopCancellation = null;
                if (!IsDisposed && !parent.IsDisposed)
                {
                    Busy(false); Refresh();
                    if (workshopClient != null) LauncherStorage.Log("SteamCMD\n" + workshopClient.Output);
                    await RefreshLibraryAsync();
                    if (closeAfterWorkshop) { closeAfterWorkshop = false; Close(); }
                }
                else workshopBusy = false;
            }
        }
        download.Click += async (_, _) =>
        {
            try { await Run(false, WorkshopStore.ParseIds(ids.Text)); } catch (Exception ex) { ShowError(ex); }
        };
        login.Click += async (_, _) => await Run(true, []);
        update.Click += async (_, _) =>
        {
            try
            {
                var tracked = WorkshopStore.Read(profile.CachePath).Select(i => i.ItemId).ToList();
                if (tracked.Count == 0) throw new InvalidOperationException(T("workshop.nothingInstalled"));
                await Run(false, tracked);
            }
            catch (Exception ex) { ShowError(ex); }
        };
        selected.Click += (_, _) =>
        {
            var wanted = mods.Where(m => selection.Ids.Contains(m.Id) && WorkshopStore.ValidId(m.WorkshopId)).Select(m => m.WorkshopId).Distinct().ToList();
            if (wanted.Count == 0) { ShowLocalizedMessage(T("workshop.noSelection"), T("workshop.tab"), false); return; }
            ids.Text = string.Join(Environment.NewLine, wanted);
        };
        cancel.Click += (_, _) => { workshopSummary = T("workshop.cancelling"); workshopCancellation?.Cancel(); };
        folder.Click += (_, _) =>
        {
            string path = Path.Combine(profile.CachePath, "mods"); Directory.CreateDirectory(path); OpenFolder(path);
        };
        logFolder.Click += (_, _) =>
        {
            string path = Path.Combine(LauncherStorage.Root, "steamcmd", "logs"); if (Directory.Exists(path)) OpenFolder(path);
        };
        Arrange(); Refresh();
    }
}
