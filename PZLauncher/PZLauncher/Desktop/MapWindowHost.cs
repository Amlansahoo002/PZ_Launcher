namespace PZLauncher.Desktop;


internal sealed class MapWindowHost : IDisposable
{
    private readonly Form owner;
    private readonly Control embedded, content;
    private readonly Func<bool> busy;
    private readonly string title;
    private bool disposing;
    internal Form? Window { get; private set; }
    internal event Action? Changed;
    internal MapWindowHost(Form owner, Control embedded, Control content, string title, Func<bool> busy)
    {
        this.owner = owner; this.embedded = embedded; this.content = content; this.title = title; this.busy = busy;
        embedded.Disposed += EmbeddedDisposed;
    }
    private void EmbeddedDisposed(object? sender, EventArgs args) => Dispose();
    internal void Toggle() { if (Window == null) Detach(); else Window.Close(); }
    internal void Detach()
    {
        if (Window != null) { Window.Activate(); return; }
        if (disposing || embedded.IsDisposed || busy()) return;
        var window = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(1280, 800),
            MinimumSize = new Size(1000, 620), BackColor = Theme.Background, ForeColor = Theme.Text, Font = Theme.Font(), Icon = owner.Icon };
        Window = window; window.Controls.Add(content); content.Dock = DockStyle.Fill;
        window.FormClosing += (_, e) =>
        {
            if (!disposing && busy()) { e.Cancel = true; return; }
            if (!disposing && !embedded.IsDisposed) { embedded.Controls.Add(content); content.BringToFront(); }
        };
        window.FormClosed += (_, _) => { Window = null; Changed?.Invoke(); };
        window.Show(owner); Changed?.Invoke();
    }
    public void Dispose()
    {
        if (disposing) return; disposing = true;
        embedded.Disposed -= EmbeddedDisposed;
        Window?.Dispose(); Window = null;
        if (!content.IsDisposed) content.Dispose();
    }
}
