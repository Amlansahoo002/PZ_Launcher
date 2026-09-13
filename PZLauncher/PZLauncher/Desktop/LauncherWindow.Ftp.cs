namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    private FtpWorldWorkspace? ftpWorld;
    private bool ConfirmLeavingFtpCopy()
    {
        if (ftpWorld == null || ftpWorld.RequiresReload) return true;
        try { if (ftpWorld.Changes().Count == 0) return true; }
        catch (IOException) {  }
        return ConfirmAction(FtpText.Get("leaveCopy", ftpWorld.LocalRoot), FtpText.Get("title")) == DialogResult.OK;
    }
    private void OpenFtpWorld()
    {
        if (worldBusy) return;
        if (!ConfirmLeavingFtpCopy()) return;
        using var dialog = new FtpWorldDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Workspace == null) return;
        ftpWorld = dialog.Workspace; selectedWorld = ftpWorld.LocalRoot; ShowPage("Sauvegardes");
    }
    private Control FtpWorldToolbar()
    {
        var bar = new ResponsiveToolbar { Dock = DockStyle.Top, Name = "FtpWorldActions" };
        var current = ftpWorld!;
        var source = Theme.Label("FTP", 9, true, Theme.AccentText); source.Width = 35; source.Height = 36; source.TextAlign = ContentAlignment.MiddleLeft;
        tips.SetToolTip(source, current.RemoteDisplay); bar.Controls.Add(source);
        ToolbarButton(bar, T("world.edit"), 220, () =>
        { using var editor = new WorldSettingsDialog(current.LocalRoot, WorldCache(), state.Installation); editor.ShowDialog(this); });
        ToolbarButton(bar, FtpText.Get("sync"), 250, async () => await SyncFtpWorld(current), true);
        ToolbarButton(bar, FtpText.Get("localCopy"), 190, () => OpenFolder(current.LocalRoot));
        return bar;
    }
    private async Task SyncFtpWorld(FtpWorldWorkspace workspace)
    {
        if (worldBusy) return;
        try
        {
            var changes = workspace.Changes();
            if (changes.Count == 0) { ShowLocalizedMessage(FtpText.Get("noChanges"), FtpText.Get("title"), false); return; }
            string description = FtpText.Get("syncConfirm", workspace.RemoteDisplay, changes.Count(c => !c.Deleted), changes.Count(c => c.Deleted)) + "\n\n" +
                string.Join('\n', changes.Take(120).Select(c => (c.Deleted ? "− " : "↥ ") + c.Relative));
            if (ConfirmAction(description, FtpText.Get("sync")) != DialogResult.OK) return;
            worldBusy = true; pageHost.Enabled = false; UpdateFooter();
            string result = await Task.Run(() => workspace.Apply(changes, new Progress<string>(text =>
            { if (!IsDisposed) BeginInvoke(() => footerDetail.Text = text); }), CancellationToken.None));
            if (!IsDisposed) ShowLocalizedMessage(result, FtpText.Get("title"), false);
        }
        catch (Exception ex) { if (!IsDisposed) ShowError(ex); }
        finally { worldBusy = false; if (!IsDisposed) { pageHost.Enabled = true; UpdateFooter(); } }
    }
}
