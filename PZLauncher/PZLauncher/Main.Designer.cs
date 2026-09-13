namespace PZLauncher
{
    partial class Main
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
            components = new System.ComponentModel.Container();
            panel1 = new Panel();
            button3 = new Button();
            btn_Logs = new Button();
            btn_Server = new Button();
            btn_Client = new Button();
            btn_Settings = new Button();
            btn_PZ = new Button();
            pz_tooltip = new ToolTip(components);
            button4 = new Button();
            label1 = new Label();
            button1 = new Button();
            button2 = new Button();
            pbx_Main = new PictureBox();
            ImageChangeTimer = new System.Windows.Forms.Timer(components);
            lbl_pbxname = new Label();
            helpProvider1 = new HelpProvider();
            panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pbx_Main).BeginInit();
            SuspendLayout();
            
            
            
            panel1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            panel1.BackColor = Color.FromArgb(41, 41, 41);
            panel1.Controls.Add(button3);
            panel1.Controls.Add(btn_Logs);
            panel1.Controls.Add(btn_Server);
            panel1.Controls.Add(btn_Client);
            panel1.Controls.Add(btn_Settings);
            panel1.Controls.Add(btn_PZ);
            panel1.Location = new Point(0, 0);
            panel1.Name = "panel1";
            panel1.Size = new Size(72, 479);
            panel1.TabIndex = 0;
            
            
            
            button3.BackColor = Color.FromArgb(64, 64, 64);
            button3.FlatAppearance.BorderSize = 0;
            button3.FlatAppearance.MouseOverBackColor = Color.FromArgb(101, 0, 2);
            button3.FlatStyle = FlatStyle.Flat;
            button3.ForeColor = Color.FromArgb(41, 41, 41);
            button3.Image = Properties.Resources.save_sp;
            button3.Location = new Point(12, 120);
            button3.Name = "button3";
            button3.Size = new Size(48, 48);
            button3.TabIndex = 5;
            pz_tooltip.SetToolTip(button3, "Server");
            button3.UseVisualStyleBackColor = false;
            button3.Click += button3_Click;
            
            
            
            btn_Logs.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btn_Logs.BackColor = Color.FromArgb(64, 64, 64);
            btn_Logs.FlatAppearance.BorderSize = 0;
            btn_Logs.FlatAppearance.MouseOverBackColor = Color.FromArgb(101, 0, 2);
            btn_Logs.FlatStyle = FlatStyle.Flat;
            btn_Logs.ForeColor = Color.FromArgb(41, 41, 41);
            btn_Logs.Image = Properties.Resources.bug;
            btn_Logs.Location = new Point(12, 365);
            btn_Logs.Name = "btn_Logs";
            btn_Logs.Size = new Size(48, 48);
            btn_Logs.TabIndex = 4;
            pz_tooltip.SetToolTip(btn_Logs, "Logs");
            btn_Logs.UseVisualStyleBackColor = false;
            btn_Logs.Click += btn_Logs_Click;
            
            
            
            btn_Server.BackColor = Color.FromArgb(64, 64, 64);
            btn_Server.FlatAppearance.BorderSize = 0;
            btn_Server.FlatAppearance.MouseOverBackColor = Color.FromArgb(101, 0, 2);
            btn_Server.FlatStyle = FlatStyle.Flat;
            btn_Server.ForeColor = Color.FromArgb(41, 41, 41);
            btn_Server.Image = Properties.Resources.database;
            btn_Server.Location = new Point(12, 174);
            btn_Server.Name = "btn_Server";
            btn_Server.Size = new Size(48, 48);
            btn_Server.TabIndex = 3;
            pz_tooltip.SetToolTip(btn_Server, "Server");
            btn_Server.UseVisualStyleBackColor = false;
            
            
            
            btn_Client.BackColor = Color.FromArgb(64, 64, 64);
            btn_Client.FlatAppearance.BorderSize = 0;
            btn_Client.FlatAppearance.MouseOverBackColor = Color.FromArgb(101, 0, 2);
            btn_Client.FlatStyle = FlatStyle.Flat;
            btn_Client.ForeColor = Color.FromArgb(41, 41, 41);
            btn_Client.Image = Properties.Resources.poste;
            btn_Client.Location = new Point(12, 66);
            btn_Client.Name = "btn_Client";
            btn_Client.Size = new Size(48, 48);
            btn_Client.TabIndex = 2;
            pz_tooltip.SetToolTip(btn_Client, "Client Settings");
            btn_Client.UseVisualStyleBackColor = false;
            btn_Client.Click += btn_Client_Click;
            
            
            
            btn_Settings.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btn_Settings.BackColor = Color.FromArgb(64, 64, 64);
            btn_Settings.FlatAppearance.BorderSize = 0;
            btn_Settings.FlatAppearance.MouseOverBackColor = Color.FromArgb(101, 0, 2);
            btn_Settings.FlatStyle = FlatStyle.Flat;
            btn_Settings.ForeColor = Color.FromArgb(41, 41, 41);
            btn_Settings.Image = Properties.Resources.settings;
            btn_Settings.Location = new Point(12, 419);
            btn_Settings.Name = "btn_Settings";
            btn_Settings.Size = new Size(48, 48);
            btn_Settings.TabIndex = 1;
            pz_tooltip.SetToolTip(btn_Settings, "Settings");
            btn_Settings.UseVisualStyleBackColor = false;
            
            
            
            btn_PZ.BackColor = Color.FromArgb(64, 64, 64);
            btn_PZ.FlatAppearance.BorderSize = 0;
            btn_PZ.FlatStyle = FlatStyle.Flat;
            btn_PZ.ForeColor = Color.FromArgb(41, 41, 41);
            btn_PZ.Image = Properties.Resources.logo;
            btn_PZ.Location = new Point(12, 12);
            btn_PZ.Name = "btn_PZ";
            btn_PZ.Size = new Size(48, 48);
            btn_PZ.TabIndex = 0;
            pz_tooltip.SetToolTip(btn_PZ, "Project Zomboid");
            btn_PZ.UseVisualStyleBackColor = false;
            btn_PZ.Click += btn_PZ_Click;
            
            
            
            button4.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            button4.BackColor = Color.FromArgb(101, 0, 2);
            button4.FlatAppearance.BorderSize = 0;
            button4.FlatStyle = FlatStyle.Flat;
            button4.Font = new Font("Segoe UI", 21.75F, FontStyle.Bold);
            button4.ForeColor = SystemColors.HighlightText;
            button4.Image = Properties.Resources.play;
            button4.ImageAlign = ContentAlignment.MiddleLeft;
            button4.Location = new Point(555, 409);
            button4.Name = "button4";
            button4.Size = new Size(158, 62);
            button4.TabIndex = 4;
            button4.Text = "PLAY";
            button4.TextAlign = ContentAlignment.MiddleRight;
            pz_tooltip.SetToolTip(button4, "Project Zomboid");
            button4.UseVisualStyleBackColor = false;
            button4.Click += button4_Click;
            
            
            
            label1.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            label1.ForeColor = SystemColors.HighlightText;
            label1.Location = new Point(78, 3);
            label1.Name = "label1";
            label1.Size = new Size(618, 27);
            label1.TabIndex = 0;
            label1.Text = "PZLauncher - v0.2.17";
            
            
            
            button1.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            button1.Location = new Point(78, 444);
            button1.Name = "button1";
            button1.Size = new Size(133, 23);
            button1.TabIndex = 7;
            button1.Text = "Create Patch";
            button1.UseVisualStyleBackColor = true;
            button1.Visible = false;
            button1.Click += button1_Click;
            
            
            
            button2.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            button2.Location = new Point(217, 444);
            button2.Name = "button2";
            button2.Size = new Size(133, 23);
            button2.TabIndex = 8;
            button2.Text = "Write Patch";
            button2.UseVisualStyleBackColor = true;
            button2.Visible = false;
            button2.Click += button2_Click;
            
            
            
            pbx_Main.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            pbx_Main.BorderStyle = BorderStyle.FixedSingle;
            pbx_Main.Location = new Point(78, 33);
            pbx_Main.Name = "pbx_Main";
            pbx_Main.Size = new Size(640, 360);
            pbx_Main.SizeMode = PictureBoxSizeMode.StretchImage;
            pbx_Main.TabIndex = 9;
            pbx_Main.TabStop = false;
            
            
            
            ImageChangeTimer.Tick += ImageChangeTimer_Tick;
            
            
            
            lbl_pbxname.Font = new Font("Segoe UI", 9.75F);
            lbl_pbxname.ForeColor = SystemColors.ControlDark;
            lbl_pbxname.Location = new Point(78, 394);
            lbl_pbxname.Name = "lbl_pbxname";
            lbl_pbxname.Size = new Size(406, 27);
            lbl_pbxname.TabIndex = 10;
            lbl_pbxname.Text = "-";
            
            
            
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(64, 64, 64);
            ClientSize = new Size(728, 479);
            Controls.Add(lbl_pbxname);
            Controls.Add(pbx_Main);
            Controls.Add(button2);
            Controls.Add(button1);
            Controls.Add(label1);
            Controls.Add(button4);
            Controls.Add(panel1);
            MinimumSize = new Size(744, 518);
            Name = "Main";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Main";
            Load += Main_Load;
            panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pbx_Main).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Panel panel1;
        private Button btn_PZ;
        private Button btn_Server;
        private ToolTip pz_tooltip;
        private Button btn_Client;
        private Button btn_Settings;
        private Button button4;
        private Label label1;
        private Button btn_Logs;
        private Button button1;
        private Button button2;
        private PictureBox pbx_Main;
        private System.Windows.Forms.Timer ImageChangeTimer;
        private Label lbl_pbxname;
        private HelpProvider helpProvider1;
        private Button button3;
    }
}