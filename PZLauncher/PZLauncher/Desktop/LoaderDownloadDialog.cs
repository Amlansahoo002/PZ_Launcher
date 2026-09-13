namespace PZLauncher.Desktop;

internal sealed class LoaderDownloadDialog : Form
{
    private readonly string loader, cache, installation;
    private readonly CancellationToken lifetime;
    private readonly Action<LoaderInstallation> installed;
    private readonly ComboBox versions = new() { DropDownStyle = ComboBoxStyle.DropDownList, Name = "LoaderVersions" };
    private readonly Label status = Theme.Label("", 9, color: Theme.Muted);
    private readonly ProgressBar progress = new() { Name = "LoaderDownloadProgress", Height = 7, Style = ProgressBarStyle.Marquee };
    private readonly ActionButton install = new(T("loaderDownload.install")) { Name = "InstallLoader", Primary = true, Width = 225, Height = 42, Enabled = false };
    private readonly ActionButton refresh = new(T("refresh")) { Width = 140, Height = 34 };
    private readonly ActionButton close = new(T("dialog.close")) { Width = 140, Height = 42 };
    private CancellationTokenSource? operation;
    private bool closePending;
    internal LoaderDownloadDialog(string loader, string cache, string installation, CancellationToken lifetime,
        Action<LoaderInstallation> installed, bool loadOnShow = true)
    {
        this.loader = loader; this.cache = cache; this.installation = installation; this.lifetime = lifetime; this.installed = installed;
        Text = T("loaderDownload.title", JavaMods.LoaderName(loader)); Name = "LoaderDownloadDialog";
        Font = Theme.Font(); ForeColor = Theme.Text; BackColor = Theme.Background;
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(740, 380); MinimumSize = new Size(740, 420); ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var heading = Theme.Label(JavaMods.LoaderName(loader), 21, true);
        var source = new LinkLabel { Text = CommunityLinks.Repository(loader), LinkColor = Theme.AccentText, ActiveLinkColor = Theme.Text,
            VisitedLinkColor = Theme.AccentText, LinkBehavior = LinkBehavior.HoverUnderline, AutoSize = false };
        source.LinkClicked += (_, _) => Open("https://github.com/" + CommunityLinks.Repository(loader));
        var note = Theme.Label(T("loaderDownload.note." + loader), 9, color: Theme.Muted);
        var destination = Theme.Label(T("loaderDownload.destination", cache), 9, color: Theme.Muted);
        var versionLabel = Theme.Label(T("loaderDownload.version"), 9);
        versions.BackColor = Theme.Surface; versions.ForeColor = Theme.Text; versions.FlatStyle = FlatStyle.Flat;
        var releaseNotes = new ActionButton(T("loaderDownload.releaseNotes")) { Width = 220, Height = 34 };
        releaseNotes.Click += (_, _) => { if (versions.SelectedItem is LoaderRelease release) Open(release.Url); };
        Controls.AddRange([heading, source, note, destination, versionLabel, versions, refresh, releaseNotes, status, progress, install, close]);
        void Arrange()
        {
            int width = ClientSize.Width - 48;
            heading.SetBounds(24, 18, width, 42); source.SetBounds(24, 63, width, 24);
            note.SetBounds(24, 96, width, 50); destination.SetBounds(24, 148, width, 38);
            versionLabel.SetBounds(24, 197, 96, 28); versions.SetBounds(124, 194, Math.Max(160, width - 476), 30);
            refresh.Location = new(versions.Right + 8, 191); releaseNotes.Location = new(refresh.Right + 8, 191);
            progress.SetBounds(24, 237, width, 7); status.SetBounds(24, 254, width, Math.Max(42, ClientSize.Height - 321));
            install.Location = new(24, ClientSize.Height - 61); close.Location = new(ClientSize.Width - close.Width - 24, ClientSize.Height - 61);
        }
        Resize += (_, _) => Arrange(); Arrange();
        refresh.Click += async (_, _) => await LoadReleasesAsync();
        install.Click += async (_, _) => await InstallAsync();
        close.Click += (_, _) => { if (operation != null) operation.Cancel(); else Close(); };
        Shown += async (_, _) => { if (loadOnShow) await LoadReleasesAsync(); };
        FormClosing += (_, e) => { if (operation != null) { closePending = true; e.Cancel = true; operation.Cancel(); } };
        versions.SelectedIndexChanged += (_, _) => install.Enabled = operation == null && versions.SelectedItem is LoaderRelease;
        if (!loadOnShow)
        {
            versions.Items.Add(new LoaderRelease(loader, loader == JavaMods.Leaf ? "v1.6.1" : "v2.3.3", "https://github.com/" + CommunityLinks.Repository(loader), [], ""));
            versions.SelectedIndex = 0; progress.Visible = false; status.Text = T("loaderDownload.choose");
        }
    }
    private static void Open(string url) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (operation != null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime); operation = cancellation;
        refresh.Enabled = versions.Enabled = install.Enabled = false; close.Text = T("dialog.cancel");
        progress.Visible = true; progress.Style = ProgressBarStyle.Marquee;
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = T("loaderDownload.cancelled"); }
        catch (Exception ex) { LauncherStorage.Log("Loader download: " + ex); if (!IsDisposed) status.Text = ex.Message; }
        finally
        {
            operation = null;
            if (!IsDisposed)
            {
                progress.Visible = false; refresh.Enabled = versions.Enabled = true; install.Enabled = versions.SelectedItem is LoaderRelease;
                close.Text = T("dialog.close"); if (closePending) Close();
            }
        }
    }
    private Task LoadReleasesAsync() => RunAsync(async token =>
    {
        status.Text = T("loaderDownload.loading");
        using var downloads = new JavaLoaderDownloads();
        var releases = await downloads.ReleasesAsync(loader, token);
        if (IsDisposed) return;
        versions.Items.Clear(); versions.Items.AddRange(releases.ToArray());
        if (releases.Count > 0) versions.SelectedIndex = 0;
        status.Text = T(releases.Count > 0 ? "loaderDownload.choose" : "loaderDownload.noReleases");
    });
    private Task InstallAsync()
    {
        if (versions.SelectedItem is not LoaderRelease release) return Task.CompletedTask;
        return RunAsync(async token =>
        {
            GameProcessGuard.EnsureStopped(cache);
            using var downloads = new JavaLoaderDownloads();
            var reporting = new Progress<LoaderTransfer>(value =>
            {
                if (IsDisposed || operation == null) return;
                progress.Style = value.Total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                if (value.Total > 0) progress.Value = (int)Math.Clamp(value.Received * 100 / value.Total.Value, 0, 100);
                status.Text = value.Total > 0 ? T("loaderDownload.progress", value.File, value.Received / 1048576d, value.Total.Value / 1048576d)
                    : value.File + "…";
            });
            status.Text = T("loaderDownload.loading");
            var result = await downloads.InstallAsync(release, cache, installation, reporting, token);
            if (IsDisposed) return;
            installed(result);
            status.Text = T("loaderDownload.installed", JavaMods.LoaderName(loader), result.Version);
        });
    }
}
