using PZ_ChunkWiper;

namespace PZLauncher.Desktop;

internal sealed partial class LauncherWindow
{
    internal void VerifyMapCalibration(string imagePath)
    {
        state.MapPreview = new();
        using var toolbar = new Panel(); using var view = new MapViewerControl();
        int previewsInvalidated = 0;
        var load = BuildMapCalibration(toolbar, view, () => previewsInvalidated++);
        load(imagePath);
        var number = Descendants(toolbar).OfType<NumericUpDown>().Single();
        var slider = Descendants(toolbar).OfType<TrackBar>().Single();
        number.Value = 4;
        if (view.BackgroundTilesPerPixel != 4 || state.MapPreview.ScaleFor(imagePath) != 4 || slider.Value != 20 || previewsInvalidated != 2)
            throw new InvalidOperationException("Map calibration controls or wipe invalidation are inconsistent.");
        slider.Value = 12;
        if (view.BackgroundTilesPerPixel != 1 || number.Value != 1 || state.MapPreview.ScaleFor(imagePath) != 1 || previewsInvalidated != 3)
            throw new InvalidOperationException("Slider did not update image calibration and invalidate preview.");
        load(imagePath);
        if (view.BackgroundTilesPerPixel != 1) throw new InvalidOperationException("Saved map scale was not reapplied.");
    }
    private Action<string> BuildMapCalibration(Control toolbar, MapViewerControl view, Action invalidatePreview)
    {
        var settings = state.MapPreview;
        string currentPath = "";
        bool syncing = false;
        var panel = new FlowLayoutPanel { Name = "MapCalibration", Width = 485, Height = 36,
            WrapContents = false, Margin = Padding.Empty, AccessibleName = T("map.scale") };
        toolbar.Controls.Add(panel);
        var slider = new TrackBar { Name = "MapScaleSlider", AccessibleName = T("map.scale"), Minimum = 0, Maximum = 36, Value = 12,
            TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 4, AutoSize = false, Width = 135, Height = 32,
            BackColor = Theme.Background, Margin = new Padding(0, 0, 4, 0), Enabled = false };
        var prefix = Theme.Label("1 px =", 9); prefix.Size = new Size(47, 29); prefix.TextAlign = ContentAlignment.MiddleLeft;
        var number = new NumericUpDown { Name = "MapScaleValue", AccessibleName = T("map.scale"), Minimum = .125m,
            Maximum = 64, DecimalPlaces = 3, Increment = .125m, Value = 1, Width = 86,
            BackColor = Theme.Surface, ForeColor = Theme.Text, Enabled = false };
        var unit = Theme.Label(T("map.tiles"), 9); unit.Size = new Size(64, 29); unit.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.AddRange([slider, prefix, number, unit]);
        var reset = ToolbarButton(panel, "1:1", 52, () => { Change(1); Save(); });
        reset.Enabled = false; reset.AccessibleName = T("map.oneToOne");
        foreach (Control control in new Control[] { panel, slider, prefix, number, unit, reset })
            tips.SetToolTip(control, T("map.scaleHelp"));
        void Save()
        {
            if (!rendering && currentPath.Length > 0)
                try { LauncherStorage.Save(state); } catch (Exception ex) { ShowError(ex); }
        }
        void Change(float value)
        {
            value = (float)Math.Round(Math.Clamp(value, .125f, 64f), 3);
            syncing = true;
            try
            {
                number.Value = (decimal)value;
                slider.Value = Math.Clamp((int)Math.Round(4 * Math.Log2(value / .125)), 0, 36);
                view.BackgroundTilesPerPixel = value;
                if (currentPath.Length > 0) settings.Remember(currentPath, value);
                invalidatePreview();
            }
            finally { syncing = false; }
        }
        slider.ValueChanged += (_, _) => { if (!syncing) Change((float)(.125 * Math.Pow(2, slider.Value / 4.0))); };
        slider.MouseUp += (_, _) => Save(); slider.KeyUp += (_, _) => Save(); slider.Validated += (_, _) => Save();
        number.ValueChanged += (_, _) => { if (!syncing) { Change((float)number.Value); Save(); } };
        void Load(string path)
        {
            string fullPath = Path.GetFullPath(path);
            view.LoadBackground(fullPath);
            currentPath = fullPath;
            Change(settings.ScaleFor(fullPath));
            slider.Enabled = number.Enabled = reset.Enabled = true;
            tips.SetToolTip(panel, T("map.scaleHelp") + Environment.NewLine + fullPath);
            Save();
        }
        if (!rendering && File.Exists(settings.ImagePath))
            try { Load(settings.ImagePath); } catch (Exception ex) { LauncherStorage.Log("Map background: " + ex); }
        return Load;
    }
}
