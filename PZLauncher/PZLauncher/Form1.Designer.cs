namespace PZLauncher
{
    partial class Form1
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            lbl_title = new Label();
            rtbx_Disclaimer = new RichTextBox();
            cbx_Disclaimer_Disagree = new CheckBox();
            cbx_Disclaimer_Agree = new CheckBox();
            btn_PZPath = new Button();
            btn_Close = new Button();
            SuspendLayout();
            
            
            
            lbl_title.Font = new Font("Segoe UI", 15.75F, FontStyle.Bold, GraphicsUnit.Point);
            lbl_title.ForeColor = SystemColors.HighlightText;
            lbl_title.Location = new Point(12, 9);
            lbl_title.Name = "lbl_title";
            lbl_title.Size = new Size(456, 31);
            lbl_title.TabIndex = 0;
            lbl_title.Text = "PZ Launcher";
            lbl_title.TextAlign = ContentAlignment.TopCenter;
            
            
            
            rtbx_Disclaimer.BackColor = SystemColors.ControlDarkDark;
            rtbx_Disclaimer.BorderStyle = BorderStyle.None;
            rtbx_Disclaimer.ForeColor = SystemColors.HighlightText;
            rtbx_Disclaimer.Location = new Point(12, 43);
            rtbx_Disclaimer.Margin = new Padding(5);
            rtbx_Disclaimer.Name = "rtbx_Disclaimer";
            rtbx_Disclaimer.ReadOnly = true;
            rtbx_Disclaimer.Size = new Size(481, 367);
            rtbx_Disclaimer.TabIndex = 1;
            rtbx_Disclaimer.Text = resources.GetString("rtbx_Disclaimer.Text");
            
            
            
            cbx_Disclaimer_Disagree.FlatStyle = FlatStyle.Flat;
            cbx_Disclaimer_Disagree.ForeColor = SystemColors.HighlightText;
            cbx_Disclaimer_Disagree.Location = new Point(170, 416);
            cbx_Disclaimer_Disagree.Name = "cbx_Disclaimer_Disagree";
            cbx_Disclaimer_Disagree.Size = new Size(90, 24);
            cbx_Disclaimer_Disagree.TabIndex = 2;
            cbx_Disclaimer_Disagree.Text = "I Disagree !";
            cbx_Disclaimer_Disagree.UseVisualStyleBackColor = true;
            cbx_Disclaimer_Disagree.CheckedChanged += cbx_Disclaimer_Disagree_CheckedChanged;
            
            
            
            cbx_Disclaimer_Agree.FlatStyle = FlatStyle.Flat;
            cbx_Disclaimer_Agree.ForeColor = SystemColors.HighlightText;
            cbx_Disclaimer_Agree.Location = new Point(266, 416);
            cbx_Disclaimer_Agree.Name = "cbx_Disclaimer_Agree";
            cbx_Disclaimer_Agree.Size = new Size(90, 24);
            cbx_Disclaimer_Agree.TabIndex = 3;
            cbx_Disclaimer_Agree.Text = "I Agree !";
            cbx_Disclaimer_Agree.UseVisualStyleBackColor = true;
            cbx_Disclaimer_Agree.CheckedChanged += cbx_Disclaimer_Agree_CheckedChanged;
            
            
            
            btn_PZPath.BackColor = SystemColors.ControlDarkDark;
            btn_PZPath.Enabled = false;
            btn_PZPath.FlatAppearance.BorderColor = Color.FromArgb(64, 64, 64);
            btn_PZPath.FlatAppearance.BorderSize = 3;
            btn_PZPath.FlatStyle = FlatStyle.Flat;
            btn_PZPath.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
            btn_PZPath.ForeColor = SystemColors.HighlightText;
            btn_PZPath.Location = new Point(101, 456);
            btn_PZPath.Name = "btn_PZPath";
            btn_PZPath.Size = new Size(287, 58);
            btn_PZPath.TabIndex = 4;
            btn_PZPath.Text = "Detect Project Zomboid";
            btn_PZPath.UseVisualStyleBackColor = false;
            btn_PZPath.Click += btn_PZPath_Click;
            
            
            
            btn_Close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btn_Close.FlatAppearance.BorderColor = SystemColors.HighlightText;
            btn_Close.FlatAppearance.BorderSize = 0;
            btn_Close.FlatStyle = FlatStyle.Flat;
            btn_Close.ForeColor = SystemColors.HighlightText;
            btn_Close.Image = Properties.Resources.close;
            btn_Close.Location = new Point(474, 1);
            btn_Close.Name = "btn_Close";
            btn_Close.Size = new Size(30, 30);
            btn_Close.TabIndex = 7;
            btn_Close.Text = "X";
            btn_Close.UseVisualStyleBackColor = true;
            btn_Close.Click += btn_Close_Click;
            
            
            
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(64, 64, 64);
            ClientSize = new Size(505, 545);
            Controls.Add(btn_Close);
            Controls.Add(btn_PZPath);
            Controls.Add(cbx_Disclaimer_Agree);
            Controls.Add(cbx_Disclaimer_Disagree);
            Controls.Add(rtbx_Disclaimer);
            Controls.Add(lbl_title);
            FormBorderStyle = FormBorderStyle.None;
            MaximumSize = new Size(505, 545);
            MinimumSize = new Size(505, 545);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Form1";
            TopMost = true;
            Load += Form1_Load;
            ResumeLayout(false);
        }

        #endregion

        private Label lbl_title;
        private RichTextBox rtbx_Disclaimer;
        private CheckBox cbx_Disclaimer_Disagree;
        private CheckBox cbx_Disclaimer_Agree;
        private Button btn_PZPath;
        private Button btn_Close;
    }
}