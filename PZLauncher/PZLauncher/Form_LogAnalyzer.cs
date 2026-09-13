using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Forms.DataVisualization.Charting;

namespace PZLauncher
{
    public partial class Form_LogAnalyzer : Form
    {
        private sealed class LogEntry
        {
            public int Line { get; set; }
            public string Time { get; set; } = "-";
            public string Level { get; set; } = "INFO";
            public string Message { get; set; } = string.Empty;
            public long Delta { get; set; }
            public string Raw { get; set; } = string.Empty;
            public long Epoch { get; set; }
            public bool Structured { get; set; }

            public string DeltaText => Delta > 0 ? Delta.ToString() : string.Empty;
            public bool IsProblem => Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase)
                || Level.Equals("WARN", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Regex LogRegexWithDate = new Regex(
            @"^\[(?<ts>[^\]]+)\]\s*(?<level>LOG|WARN|ERROR|DEBUG)\s*:[^\>]*t:(?<epoch>\d{13})>\s*(?<msg>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LogRegexRaw = new Regex(
            @"^(?<level>LOG|WARN|ERROR|DEBUG)\s*:[^\>]*t:(?<epoch>\d{13})>\s*(?<msg>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly string? initialLogPath;
        private readonly List<LogEntry> logEntries = new List<LogEntry>();
        private readonly BindingList<LogEntry> filteredEntries = new BindingList<LogEntry>();
        private string? currentLogPath;
        private ComboBox cmbLevelFilter = new ComboBox();
        private CheckBox cbxSlowOnly = new CheckBox();
        private Button btn_Reload = new Button();
        private Button btn_OpenFolder = new Button();

        public Form_LogAnalyzer() : this(null)
        {
        }

        public Form_LogAnalyzer(string? initialLogPath)
        {
            this.initialLogPath = initialLogPath;
            InitializeComponent();
            InitializeLogGrid();
            InitializeLogViewerUi();
        }

        private void InitializeLogViewerUi()
        {
            TopMost = false;
            Text = "PZLauncher - Logs";
            btn_OpenLogs.Text = "OPEN";
            label1.AutoSize = false;
            label1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            label1.Size = new Size(ClientSize.Width - 240, 25);

            tbx_LogsSearch.Width = 285;
            btn_LogsSearch.Location = new Point(tbx_LogsSearch.Right + 6, btn_LogsSearch.Top);

            cmbLevelFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(btn_LogsSearch.Right + 10, 16),
                Size = new Size(96, 23)
            };
            cmbLevelFilter.Items.AddRange(new object[] { "Tous", "Problemes", "Errors", "Warnings", "Slow", "Info/Log" });
            cmbLevelFilter.SelectedIndex = 0;
            cmbLevelFilter.SelectedIndexChanged += (sender, args) => ApplyLogFilter();
            groupBox1.Controls.Add(cmbLevelFilter);

            cbxSlowOnly = new CheckBox
            {
                AutoSize = true,
                ForeColor = Color.White,
                Location = new Point(cmbLevelFilter.Right + 10, 18),
                Text = "Slow only"
            };
            cbxSlowOnly.CheckedChanged += (sender, args) => ApplyLogFilter();
            groupBox1.Controls.Add(cbxSlowOnly);

            btn_Reload = CreateHeaderButton("RELOAD", 12, 51, 86);
            btn_Reload.Click += (sender, args) => ReloadCurrentLog();
            Controls.Add(btn_Reload);

            btn_OpenFolder = CreateHeaderButton("FOLDER", 104, 51, 85);
            btn_OpenFolder.Click += (sender, args) => OpenCurrentLogFolder();
            Controls.Add(btn_OpenFolder);

            tbx_LogsSearch.TextChanged += (sender, args) => ApplyLogFilter();
            dataGridView1.RowPrePaint += dataGridView1_RowPrePaint;
        }

        private static Button CreateHeaderButton(string text, int x, int y, int width)
        {
            var button = new Button
            {
                BackColor = Color.FromArgb(41, 41, 41),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                Location = new Point(x, y),
                Size = new Size(width, 23),
                Text = text,
                UseVisualStyleBackColor = false
            };

            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private void InitializeLogGrid()
        {
            dataGridView1.Columns.Clear();
            dataGridView1.AutoGenerateColumns = false;
            dataGridView1.AllowUserToAddRows = false;
            dataGridView1.AllowUserToDeleteRows = false;
            dataGridView1.AllowUserToResizeRows = false;
            dataGridView1.ReadOnly = true;
            dataGridView1.MultiSelect = false;
            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView1.RowHeadersVisible = false;
            dataGridView1.BackgroundColor = Color.FromArgb(34, 37, 42);
            dataGridView1.GridColor = Color.FromArgb(55, 60, 68);
            dataGridView1.DefaultCellStyle.BackColor = Color.FromArgb(30, 32, 36);
            dataGridView1.DefaultCellStyle.ForeColor = Color.FromArgb(226, 230, 236);
            dataGridView1.DefaultCellStyle.SelectionBackColor = Color.FromArgb(82, 58, 62);
            dataGridView1.DefaultCellStyle.SelectionForeColor = Color.White;

            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(LogEntry.Line),
                HeaderText = "#",
                Width = 64,
                ReadOnly = true
            });

            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(LogEntry.Time),
                HeaderText = "Time",
                Width = 120,
                ReadOnly = true
            });

            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(LogEntry.Level),
                HeaderText = "Level",
                Width = 82,
                ReadOnly = true
            });

            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(LogEntry.DeltaText),
                HeaderText = "Delta",
                Width = 82,
                ReadOnly = true,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
            });

            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(LogEntry.Message),
                HeaderText = "Message",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                ReadOnly = true
            });

            dataGridView1.DataSource = filteredEntries;
        }

        private void Form_LogAnalyzer_Load(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(initialLogPath) && File.Exists(initialLogPath))
            {
                LoadAndDisplayLog(initialLogPath);
            }
            else
            {
                label1.Text = "Aucun log charge";
                UpdateLogStats();
                FillChart();
            }
        }

        private void btn_OpenLogs_Click(object sender, EventArgs e)
        {
            string preferredPath = !string.IsNullOrWhiteSpace(initialLogPath)
                ? initialLogPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"Zomboid\console.txt");

            if (File.Exists(preferredPath))
            {
                LoadAndDisplayLog(preferredPath);
                return;
            }

            using var dialog = new OpenFileDialog
            {
                Filter = "Project Zomboid logs|console.txt;*.txt|All files|*.*",
                Title = "Open Project Zomboid log"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                LoadAndDisplayLog(dialog.FileName);
            }
        }

        private void ReloadCurrentLog()
        {
            if (!string.IsNullOrWhiteSpace(currentLogPath) && File.Exists(currentLogPath))
            {
                LoadAndDisplayLog(currentLogPath);
            }
        }

        private void OpenCurrentLogFolder()
        {
            if (string.IsNullOrWhiteSpace(currentLogPath))
            {
                return;
            }

            string? folder = Path.GetDirectoryName(currentLogPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folder}\"",
                    UseShellExecute = true
                });
            }
        }

        private void LoadAndDisplayLog(string path)
        {
            currentLogPath = path;
            label1.Text = path;
            logEntries.Clear();
            filteredEntries.Clear();

            long lastEpoch = -1;
            int lineNumber = 0;

            foreach (string line in File.ReadLines(path))
            {
                lineNumber++;
                logEntries.Add(ParseLogLine(line, lineNumber, ref lastEpoch));
            }

            ApplyLogFilter();
        }

        private static LogEntry ParseLogLine(string line, int lineNumber, ref long lastEpoch)
        {
            Match match = LogRegexWithDate.Match(line);
            if (!match.Success)
            {
                match = LogRegexRaw.Match(line);
            }

            if (match.Success)
            {
                string level = match.Groups["level"].Value.ToUpperInvariant();
                long epoch = long.Parse(match.Groups["epoch"].Value);
                long delta = lastEpoch > 0 ? epoch - lastEpoch : 0;
                lastEpoch = epoch;

                return new LogEntry
                {
                    Line = lineNumber,
                    Epoch = epoch,
                    Time = DateTimeOffset.FromUnixTimeMilliseconds(epoch).LocalDateTime.ToString("HH:mm:ss.fff"),
                    Level = level,
                    Message = match.Groups["msg"].Value.Trim(),
                    Delta = delta,
                    Raw = line,
                    Structured = true
                };
            }

            return new LogEntry
            {
                Line = lineNumber,
                Time = "-",
                Level = InferLevel(line),
                Message = line.Trim(),
                Raw = line,
                Structured = false
            };
        }

        private static string InferLevel(string line)
        {
            if (Contains(line, "ERROR") || line.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                || Contains(line, "Exception") || Contains(line, "Caused by:"))
            {
                return "ERROR";
            }

            if (Contains(line, "WARN"))
            {
                return "WARN";
            }

            if (Contains(line, "DEBUG"))
            {
                return "DEBUG";
            }

            if (Contains(line, "LOG"))
            {
                return "LOG";
            }

            return "INFO";
        }

        private static bool Contains(string value, string fragment)
        {
            return value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyLogFilter()
        {
            string keyword = tbx_LogsSearch.Text.Trim();
            int deltaThreshold = (int)nud_LogSearch.Value;
            string levelFilter = cmbLevelFilter.SelectedItem?.ToString() ?? "Tous";

            IEnumerable<LogEntry> query = logEntries;

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(entry =>
                    entry.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || entry.Level.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || entry.Raw.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            query = levelFilter switch
            {
                "Problemes" => query.Where(entry => entry.IsProblem),
                "Errors" => query.Where(entry => entry.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase)),
                "Warnings" => query.Where(entry => entry.Level.Equals("WARN", StringComparison.OrdinalIgnoreCase)),
                "Slow" => query.Where(entry => entry.Delta >= deltaThreshold),
                "Info/Log" => query.Where(entry => !entry.IsProblem),
                _ => query
            };

            if (cbxSlowOnly.Checked)
            {
                query = query.Where(entry => entry.Delta >= deltaThreshold);
            }

            filteredEntries.RaiseListChangedEvents = false;
            filteredEntries.Clear();
            foreach (LogEntry entry in query)
            {
                filteredEntries.Add(entry);
            }

            filteredEntries.RaiseListChangedEvents = true;
            filteredEntries.ResetBindings();

            UpdateLogStats();
            FillChart();
        }

        private void UpdateLogStats()
        {
            int total = logEntries.Count;
            int displayed = filteredEntries.Count;
            int warn = logEntries.Count(entry => entry.Level.Equals("WARN", StringComparison.OrdinalIgnoreCase));
            int error = logEntries.Count(entry => entry.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase));
            long maxDelta = logEntries.Count > 0 ? logEntries.Max(entry => entry.Delta) : 0;
            long slow = logEntries.Count(entry => entry.Delta >= (int)nud_LogSearch.Value);

            lbl_Stats_Total.Text = $"Affichees : {displayed}/{total}";
            lbl_Stats_ErrWarn.Text = $"WARN {warn} | ERROR {error}";
            lbl_Stats_MaxDelta.Text = $"Delta max : {maxDelta} ms";
            lbl_Stats_AvgLast10.Text = $"Slow : {slow}";
        }

        private void FillChart()
        {
            chart1.Series.Clear();
            chart1.Legends.Clear();

            var area = chart1.ChartAreas[0];
            Color background = Color.FromArgb(30, 32, 36);
            chart1.BackColor = background;
            area.BackColor = background;
            area.AxisX.MajorGrid.Enabled = false;
            area.AxisY.MajorGrid.Enabled = false;
            area.AxisX.LabelStyle.Enabled = false;
            area.AxisY.LabelStyle.Enabled = false;
            area.AxisX.LineColor = Color.FromArgb(70, 76, 84);
            area.AxisY.LineColor = Color.FromArgb(70, 76, 84);
            area.AxisY.IsReversed = true;

            var series = new Series("Delta")
            {
                ChartType = SeriesChartType.Point,
                MarkerSize = 4,
                Color = Color.FromArgb(95, 185, 125)
            };
            chart1.Series.Add(series);

            var selected = new Series("Selection")
            {
                ChartType = SeriesChartType.Point,
                MarkerSize = 10,
                Color = Color.Cyan
            };
            chart1.Series.Add(selected);

            List<LogEntry> chartEntries = filteredEntries
                .Where(entry => entry.Delta > 0 || entry.IsProblem)
                .TakeLast(5000)
                .ToList();

            for (int i = 0; i < chartEntries.Count; i++)
            {
                LogEntry entry = chartEntries[i];
                int index = series.Points.AddXY(entry.Delta, i);
                series.Points[index].Tag = entry;
                series.Points[index].Color = GetEntryColor(entry);
            }
        }

        private static Color GetEntryColor(LogEntry entry)
        {
            if (entry.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromArgb(230, 82, 82);
            }

            if (entry.Level.Equals("WARN", StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromArgb(240, 188, 75);
            }

            if (entry.Delta >= 100)
            {
                return Color.FromArgb(255, 148, 72);
            }

            return Color.FromArgb(95, 185, 125);
        }

        private void chart1_MouseClick(object sender, MouseEventArgs e)
        {
            var hit = chart1.HitTest(e.X, e.Y);

            if (hit.ChartElementType != ChartElementType.DataPoint || hit.Series?.Name != "Delta")
            {
                return;
            }

            if (hit.Series.Points[hit.PointIndex].Tag is not LogEntry entry)
            {
                return;
            }

            SelectEntry(entry);
        }

        private void SelectEntry(LogEntry entry)
        {
            for (int i = 0; i < dataGridView1.Rows.Count; i++)
            {
                if (ReferenceEquals(dataGridView1.Rows[i].DataBoundItem, entry))
                {
                    dataGridView1.ClearSelection();
                    dataGridView1.Rows[i].Selected = true;
                    dataGridView1.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 4);
                    HighlightChartPoint(entry);
                    return;
                }
            }
        }

        private void HighlightChartPoint(LogEntry entry)
        {
            if (chart1.Series.IsUniqueName("Delta") || chart1.Series.IsUniqueName("Selection"))
            {
                return;
            }

            DataPoint? chartPoint = chart1.Series["Delta"].Points
                .FirstOrDefault(point => ReferenceEquals(point.Tag, entry));

            if (chartPoint == null)
            {
                chart1.Series["Selection"].Points.Clear();
                return;
            }

            var markerSeries = chart1.Series["Selection"];
            markerSeries.Points.Clear();
            int pointIndex = markerSeries.Points.AddXY(chartPoint.XValue, chartPoint.YValues[0]);
            var marker = markerSeries.Points[pointIndex];
            marker.Color = Color.Cyan;
            marker.BorderColor = Color.White;
            marker.BorderWidth = 2;
            marker.MarkerStyle = MarkerStyle.Circle;
        }

        private void dataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
            {
                return;
            }

            if (dataGridView1.SelectedRows[0].DataBoundItem is LogEntry entry)
            {
                HighlightChartPoint(entry);
            }
        }

        private void dataGridView1_RowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
        {
            if (e.RowIndex < 0 || dataGridView1.Rows[e.RowIndex].DataBoundItem is not LogEntry entry)
            {
                return;
            }

            DataGridViewRow row = dataGridView1.Rows[e.RowIndex];
            row.DefaultCellStyle.BackColor = Color.FromArgb(30, 32, 36);
            row.DefaultCellStyle.ForeColor = Color.FromArgb(226, 230, 236);

            if (entry.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(96, 38, 42);
                row.DefaultCellStyle.ForeColor = Color.White;
            }
            else if (entry.Level.Equals("WARN", StringComparison.OrdinalIgnoreCase))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(94, 72, 32);
                row.DefaultCellStyle.ForeColor = Color.White;
            }
            else if (entry.Delta >= (int)nud_LogSearch.Value && entry.Delta > 0)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(64, 52, 42);
            }
        }

        private void tbx_LogsSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ApplyLogFilter();
            }
        }

        private void btn_LogsSearch_Click(object sender, EventArgs e)
        {
            ApplyLogFilter();
        }

        private void nud_LogSearch_ValueChanged(object sender, EventArgs e)
        {
            ApplyLogFilter();
        }

        private void btn_ResetZoom_Click(object sender, EventArgs e)
        {
            if (chart1.ChartAreas.Count == 0)
            {
                return;
            }

            var area = chart1.ChartAreas[0];
            area.AxisX.ScaleView.ZoomReset();
            area.AxisY.ScaleView.ZoomReset();
        }

        private void btn_Close_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
