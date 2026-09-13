using System.Collections.ObjectModel;

namespace PZLauncher.Desktop;

internal sealed class ThemedTabs : Panel
{
    private readonly FlowLayoutPanel header = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
    private readonly Panel body = new() { Dock = DockStyle.Fill };
    private readonly List<ActionButton> buttons = [];
    internal PageCollection TabPages { get; }
    internal int TabCount => TabPages.Count;
    private int selected = -1;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool SelectionLocked { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex
    {
        get => selected;
        set
        {
            if (SelectionLocked || value < 0 || value >= TabPages.Count || value == selected) return;
            selected = value;
            for (int i = 0; i < TabPages.Count; i++)
            {
                TabPages[i].Visible = i == value;
                buttons[i].Active = i == value; buttons[i].Invalidate();
            }
            TabPages[value].BringToFront();
        }
    }
    internal ThemedTabs()
    {
        BackColor = Theme.Background; TabPages = new PageCollection(this);
        Controls.Add(body); Controls.Add(header);
    }
    internal sealed class PageCollection(ThemedTabs owner) : Collection<Panel>
    {
        protected override void InsertItem(int index, Panel page)
        {
            base.InsertItem(index, page);
            page.Dock = DockStyle.Fill; page.Visible = false; owner.body.Controls.Add(page);
            var button = new ActionButton(page.Text) { Tab = true, Font = Theme.Font(9.5f, true), Height = 36,
                Margin = new Padding(0, 0, 3, 3), AccessibleRole = AccessibleRole.PageTab };
            button.Width = TextRenderer.MeasureText(page.Text, button.Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width + 36;
            button.Click += (_, _) => owner.SelectedIndex = IndexOf(page);
            owner.buttons.Insert(index, button); owner.header.Controls.Add(button);
            if (Count == 1) owner.SelectedIndex = 0;
        }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (TabCount > 0 && keyData is (Keys.Control | Keys.Tab) or (Keys.Control | Keys.Shift | Keys.Tab))
        {
            SelectedIndex = (SelectedIndex + ((keyData & Keys.Shift) != 0 ? TabCount - 1 : 1)) % TabCount; return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
