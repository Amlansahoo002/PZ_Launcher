namespace PZLauncher
{
    partial class Form_LogAnalyzer
    {
                private System.ComponentModel.IContainer components = null;

                protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

                private void InitializeComponent()
        {
            System.Windows.Forms.DataVisualization.Charting.ChartArea chartArea1 = new System.Windows.Forms.DataVisualization.Charting.ChartArea();
            System.Windows.Forms.DataVisualization.Charting.Series series1 = new System.Windows.Forms.DataVisualization.Charting.Series();
            dataGridView1 = new DataGridView();
            chart1 = new System.Windows.Forms.DataVisualization.Charting.Chart();
            btn_OpenLogs = new Button();
            splitContainer1 = new SplitContainer();
            groupBox1 = new GroupBox();
            label3 = new Label();
            nud_LogSearch = new NumericUpDown();
            btn_LogsSearch = new Button();
            tbx_LogsSearch = new TextBox();
            label2 = new Label();
            label1 = new Label();
            statusStrip1 = new StatusStrip();
            lbl_Stats_Total = new ToolStripStatusLabel();
            lbl_Stats_ErrWarn = new ToolStripStatusLabel();
            lbl_Stats_MaxDelta = new ToolStripStatusLabel();
            lbl_Stats_AvgLast10 = new ToolStripStatusLabel();
            btn_ResetZoom = new Button();
            ((System.ComponentModel.ISupportInitialize)dataGridView1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)chart1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nud_LogSearch).BeginInit();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            
            
            
            dataGridView1.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridView1.Dock = DockStyle.Fill;
            dataGridView1.Location = new Point(0, 0);
            dataGridView1.Name = "dataGridView1";
            dataGridView1.Size = new Size(758, 602);
            dataGridView1.TabIndex = 9;
            dataGridView1.SelectionChanged += dataGridView1_SelectionChanged;
            
            
            
            chartArea1.Name = "ChartArea1";
            chart1.ChartAreas.Add(chartArea1);
            chart1.Dock = DockStyle.Fill;
            chart1.Location = new Point(0, 0);
            chart1.Name = "chart1";
            series1.ChartArea = "ChartArea1";
            series1.Name = "Series1";
            chart1.Series.Add(series1);
            chart1.Size = new Size(222, 602);
            chart1.TabIndex = 10;
            chart1.Text = "chart1";
            chart1.MouseClick += chart1_MouseClick;
            
            
            
            btn_OpenLogs.BackColor = Color.FromArgb(41, 41, 41);
            btn_OpenLogs.FlatAppearance.BorderSize = 0;
            btn_OpenLogs.FlatStyle = FlatStyle.Flat;
            btn_OpenLogs.Font = new Font("Segoe UI", 14.25F, FontStyle.Bold);
            btn_OpenLogs.ForeColor = SystemColors.HighlightText;
            btn_OpenLogs.ImageAlign = ContentAlignment.MiddleLeft;
            btn_OpenLogs.Location = new Point(12, 8);
            btn_OpenLogs.Name = "btn_OpenLogs";
            btn_OpenLogs.Size = new Size(177, 37);
            btn_OpenLogs.TabIndex = 19;
            btn_OpenLogs.Text = "OPEN LOGS";
            btn_OpenLogs.UseVisualStyleBackColor = false;
            btn_OpenLogs.Click += btn_OpenLogs_Click;
            
            
            
            splitContainer1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            splitContainer1.Location = new Point(12, 102);
            splitContainer1.Name = "splitContainer1";
            
            
            
            splitContainer1.Panel1.Controls.Add(chart1);
            
            
            
            splitContainer1.Panel2.Controls.Add(dataGridView1);
            splitContainer1.Size = new Size(984, 602);
            splitContainer1.SplitterDistance = 222;
            splitContainer1.TabIndex = 20;
            
            
            
            groupBox1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            groupBox1.Controls.Add(label3);
            groupBox1.Controls.Add(nud_LogSearch);
            groupBox1.Controls.Add(btn_LogsSearch);
            groupBox1.Controls.Add(tbx_LogsSearch);
            groupBox1.Controls.Add(label2);
            groupBox1.Location = new Point(238, 48);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(758, 48);
            groupBox1.TabIndex = 10;
            groupBox1.TabStop = false;
            
            
            
            label3.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            label3.AutoSize = true;
            label3.ForeColor = SystemColors.HighlightText;
            label3.Location = new Point(616, 19);
            label3.Name = "label3";
            label3.Size = new Size(61, 15);
            label3.TabIndex = 24;
            label3.Text = "Delta (ms)";
            
            
            
            nud_LogSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            nud_LogSearch.Location = new Point(683, 16);
            nud_LogSearch.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            nud_LogSearch.Name = "nud_LogSearch";
            nud_LogSearch.Size = new Size(69, 23);
            nud_LogSearch.TabIndex = 23;
            nud_LogSearch.Value = new decimal(new int[] { 100, 0, 0, 0 });
            nud_LogSearch.ValueChanged += nud_LogSearch_ValueChanged;
            
            
            
            btn_LogsSearch.BackColor = Color.FromArgb(41, 41, 41);
            btn_LogsSearch.FlatAppearance.BorderSize = 0;
            btn_LogsSearch.FlatStyle = FlatStyle.Flat;
            btn_LogsSearch.Font = new Font("Segoe UI", 14.25F, FontStyle.Bold);
            btn_LogsSearch.ForeColor = SystemColors.HighlightText;
            btn_LogsSearch.Image = Properties.Resources.FindInFile_16x;
            btn_LogsSearch.Location = new Point(474, 16);
            btn_LogsSearch.Name = "btn_LogsSearch";
            btn_LogsSearch.Size = new Size(33, 23);
            btn_LogsSearch.TabIndex = 22;
            btn_LogsSearch.UseVisualStyleBackColor = false;
            btn_LogsSearch.Click += btn_LogsSearch_Click;
            
            
            
            tbx_LogsSearch.Location = new Point(54, 16);
            tbx_LogsSearch.Name = "tbx_LogsSearch";
            tbx_LogsSearch.Size = new Size(414, 23);
            tbx_LogsSearch.TabIndex = 1;
            tbx_LogsSearch.KeyDown += tbx_LogsSearch_KeyDown;
            
            
            
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.HighlightText;
            label2.Location = new Point(6, 19);
            label2.Name = "label2";
            label2.Size = new Size(42, 15);
            label2.TabIndex = 0;
            label2.Text = "Search";
            
            
            
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 14.25F, FontStyle.Bold);
            label1.ForeColor = SystemColors.HighlightText;
            label1.Location = new Point(195, 14);
            label1.Name = "label1";
            label1.Size = new Size(27, 25);
            label1.TabIndex = 21;
            label1.Text = "...";
            
            
            
            statusStrip1.Items.AddRange(new ToolStripItem[] { lbl_Stats_Total, lbl_Stats_ErrWarn, lbl_Stats_MaxDelta, lbl_Stats_AvgLast10 });
            statusStrip1.LayoutStyle = ToolStripLayoutStyle.Flow;
            statusStrip1.Location = new Point(0, 709);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(1008, 20);
            statusStrip1.TabIndex = 22;
            statusStrip1.Text = "statusStrip1";
            
            
            
            lbl_Stats_Total.BackColor = Color.Transparent;
            lbl_Stats_Total.Name = "lbl_Stats_Total";
            lbl_Stats_Total.Size = new Size(80, 15);
            lbl_Stats_Total.Text = "lbl_Stats_Total";
            
            
            
            lbl_Stats_ErrWarn.BackColor = Color.Transparent;
            lbl_Stats_ErrWarn.Name = "lbl_Stats_ErrWarn";
            lbl_Stats_ErrWarn.Size = new Size(97, 15);
            lbl_Stats_ErrWarn.Text = "lbl_Stats_ErrWarn";
            
            
            
            lbl_Stats_MaxDelta.BackColor = Color.Transparent;
            lbl_Stats_MaxDelta.Name = "lbl_Stats_MaxDelta";
            lbl_Stats_MaxDelta.Size = new Size(105, 15);
            lbl_Stats_MaxDelta.Text = "lbl_Stats_MaxDelta";
            
            
            
            lbl_Stats_AvgLast10.BackColor = Color.Transparent;
            lbl_Stats_AvgLast10.Name = "lbl_Stats_AvgLast10";
            lbl_Stats_AvgLast10.Size = new Size(109, 15);
            lbl_Stats_AvgLast10.Text = "lbl_Stats_AvgLast10";
            
            
            
            btn_ResetZoom.BackColor = Color.FromArgb(41, 41, 41);
            btn_ResetZoom.FlatAppearance.BorderSize = 0;
            btn_ResetZoom.FlatStyle = FlatStyle.Flat;
            btn_ResetZoom.Font = new Font("Segoe UI", 14.25F, FontStyle.Bold);
            btn_ResetZoom.ForeColor = SystemColors.HighlightText;
            btn_ResetZoom.Image = Properties.Resources.ZoomToFit_16x;
            btn_ResetZoom.Location = new Point(12, 73);
            btn_ResetZoom.Name = "btn_ResetZoom";
            btn_ResetZoom.Size = new Size(33, 23);
            btn_ResetZoom.TabIndex = 25;
            btn_ResetZoom.UseVisualStyleBackColor = false;
            btn_ResetZoom.Click += btn_ResetZoom_Click;
            
            
            
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(64, 64, 64);
            ClientSize = new Size(1008, 729);
            Controls.Add(btn_ResetZoom);
            Controls.Add(statusStrip1);
            Controls.Add(label1);
            Controls.Add(groupBox1);
            Controls.Add(splitContainer1);
            Controls.Add(btn_OpenLogs);
            MinimumSize = new Size(1024, 768);
            Name = "Form_LogAnalyzer";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Form_LogAnalyzer";
            TopMost = true;
            Load += Form_LogAnalyzer_Load;
            ((System.ComponentModel.ISupportInitialize)dataGridView1).EndInit();
            ((System.ComponentModel.ISupportInitialize)chart1).EndInit();
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nud_LogSearch).EndInit();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private DataGridView dataGridView1;
        private System.Windows.Forms.DataVisualization.Charting.Chart chart1;
        private Button btn_OpenLogs;
        private SplitContainer splitContainer1;
        private GroupBox groupBox1;
        private Label label1;
        private Button btn_LogsSearch;
        private TextBox tbx_LogsSearch;
        private Label label2;
        private Label label3;
        private NumericUpDown nud_LogSearch;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel lbl_Stats_Total;
        private ToolStripStatusLabel lbl_Stats_ErrWarn;
        private ToolStripStatusLabel lbl_Stats_MaxDelta;
        private ToolStripStatusLabel lbl_Stats_AvgLast10;
        private Button btn_ResetZoom;
    }
}