using System.Globalization;

namespace PZLauncher.Desktop;

internal sealed class ConfigurationFields : UserControl
{
    private readonly DataGridView grid = Theme.Grid();
    private readonly TextBox search;
    private readonly Label description;
    private readonly Dictionary<string, string> values;
    private readonly Dictionary<string, ConfigOption> options;
    private readonly Action<string, string, string> changed;
    internal ConfigurationFields(IEnumerable<ConfigOption> schema, Dictionary<string, string> present, Action<string, string, string> onChanged)
    {
        Dock = DockStyle.Fill; BackColor = Theme.Background;
        grid.RowTemplate.Height = 32; grid.ColumnHeadersHeight = 34;
        values = new(present); options = schema.ToDictionary(o => o.Name); changed = onChanged;
        foreach (var p in present) if (!options.ContainsKey(p.Key)) options[p.Key] = ConfigOption.Infer(p.Key, p.Value);
        search = new TextBox { Dock = DockStyle.Top, PlaceholderText = T("config.search"), BackColor = Theme.Surface, ForeColor = Theme.Text };
        description = Theme.Label(T("config.defaults"), 8.5f, color: Theme.Muted); description.Dock = DockStyle.Bottom; description.Height = 60;
        var help = new ToolTip { AutoPopDelay = 30000 }; Disposed += (_, _) => help.Dispose();
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Option", HeaderText = T("config.option"), ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 50 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = T("config.value"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 36 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Origin", HeaderText = T("config.origin"), ReadOnly = true, Width = 125 });
        Controls.Add(grid); Controls.Add(search); Controls.Add(description);
        foreach (var option in options.Values)
        {
            string value = values.GetValueOrDefault(option.Name, option.Default);
            int row = grid.Rows.Add(option.Title is null || option.Title == option.Name ? option.Name : option.Title + " · " + option.Name, value, T(present.ContainsKey(option.Name) ? "config.inFile" : "config.default"));
            var r = grid.Rows[row]; r.Tag = option; r.Cells[0].ToolTipText = option.Name + "\n" + option.Tooltip;
            if (option.Type == "boolean" && value is "true" or "false") r.Cells[1] = new ConfigurationCheckBoxCell { Value = value == "true" };
            else if (option.Choices.Count > 0)
            {
                var choices = option.Choices.ToList(); if (!choices.Any(c => c.Value == value)) choices.Insert(0, new(value, value + " · " + T("config.invalidStored")));
                r.Cells[1] = new DataGridViewComboBoxCell { DataSource = choices, ValueMember = "Value", DisplayMember = "Label", Value = value, FlatStyle = FlatStyle.Flat };
            }
            r.Cells[1].ToolTipText = T("config.constraints", option.Type, option.Min?.ToString(CultureInfo.InvariantCulture) ?? "…", option.Max?.ToString(CultureInfo.InvariantCulture) ?? "…", option.Default);
        }
        search.TextChanged += (_, _) =>
        {
            grid.CurrentCell = null;
            foreach (DataGridViewRow row in grid.Rows) row.Visible = row.Cells[0].Value?.ToString()?.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) == true;
        };
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.CurrentRow?.Tag is ConfigOption o)
            {
                description.Text = o.Name + " · " + T("config.constraints", o.Type, o.Min?.ToString(CultureInfo.InvariantCulture) ?? "…", o.Max?.ToString(CultureInfo.InvariantCulture) ?? "…", o.Default) + "\n" + o.Tooltip;
                help.SetToolTip(description, description.Text + "\n\n" + T("config.defaults"));
            }
        };
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValidating += (_, e) =>
        {
            if (e.ColumnIndex != 1 || grid.Rows[e.RowIndex].Tag is not ConfigOption o || grid.CurrentCell is DataGridViewComboBoxCell) return;
            string value = grid.CurrentCell is DataGridViewCheckBoxCell ? Convert.ToBoolean(e.FormattedValue).ToString().ToLowerInvariant() : e.FormattedValue?.ToString() ?? "";
            try { if (value != values.GetValueOrDefault(o.Name, o.Default)) o.Validate(value); grid.Rows[e.RowIndex].ErrorText = ""; }
            catch (Exception ex) { grid.Rows[e.RowIndex].ErrorText = ex.Message; description.Text = ex.Message; e.Cancel = true; }
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 1 || grid.Rows[e.RowIndex].Tag is not ConfigOption o) return;
            object? cell = grid.Rows[e.RowIndex].Cells[1].Value;
            string value = cell is bool b ? b.ToString().ToLowerInvariant() : cell?.ToString() ?? "";
            if (value == values.GetValueOrDefault(o.Name, o.Default)) return;
            try { o.Validate(value); changed(o.Name, value, o.Type); values[o.Name] = value; grid.Rows[e.RowIndex].Cells[2].Value = T("config.modified"); grid.Rows[e.RowIndex].ErrorText = ""; }
            catch (Exception ex) { grid.Rows[e.RowIndex].ErrorText = ex.Message; description.Text = ex.Message; }
        };
        grid.DataError += (_, e) => { e.ThrowException = false; e.Cancel = true; };
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 1 || grid.Rows[e.RowIndex].Cells[1] is not ConfigurationCheckBoxCell checkbox || e.Graphics == null) return;
            e.PaintBackground(e.ClipBounds, true);
            Theme.DrawCheck(e.Graphics, checkbox.CheckBounds(e.CellBounds), Convert.ToBoolean(e.FormattedValue), grid.Enabled && !checkbox.ReadOnly);
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus); e.Handled = true;
        };
    }
    internal void Commit()
    {
        if (!grid.EndEdit() || grid.Rows.Cast<DataGridViewRow>().Any(r => r.ErrorText.Length > 0)) throw new InvalidDataException(T("server.invalid"));
    }
}

internal sealed class ConfigurationCheckBoxCell : DataGridViewCheckBoxCell
{
    public ConfigurationCheckBoxCell()
    {
        Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
    }

    internal Rectangle CheckBounds(Rectangle cellBounds)
    {
        int size = DataGridView?.LogicalToDeviceUnits(18) ?? 18;
        return new Rectangle(cellBounds.X + (cellBounds.Width - size) / 2,
            cellBounds.Y + (cellBounds.Height - size) / 2, size, size);
    }

    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex)
    {
        if (DataGridView == null || rowIndex < 0 || OwningColumn == null) return Rectangle.Empty;
        
        
        return CheckBounds(new Rectangle(Point.Empty, GetSize(rowIndex)));
    }
}
