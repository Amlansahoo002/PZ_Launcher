using Microsoft.VisualBasic.Devices;
using PZLauncher.Profiles;
using PZLauncher.Services;
using PZLauncher.utils;
using System.ComponentModel;
using System.Runtime.InteropServices;


namespace PZLauncher
{
    public partial class ClientSettings : Form
    {
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn
(
    int nLeftRect, 
    int nTopRect, 
    int nRightRect, 
    int nBottomRect, 
    int nWidthEllipse, 
    int nHeightEllipse 
);
        public ulong InstalledRam { get; set; }
        IniFile iniFile = new IniFile(Settings.Actual_Path + @"\settings.ini");
        private readonly GameProfile? gameProfile;
        private readonly OptionsIniService optionsIniService = new OptionsIniService();
        private readonly List<OptionEntry> optionEntries = new List<OptionEntry>();
        private readonly BindingList<OptionEntry> visibleOptionEntries = new BindingList<OptionEntry>();
        private string optionsIniPath = string.Empty;
        private DataGridView dgvOptions = new DataGridView();
        private TextBox tbxOptionsFilter = new TextBox();
        private Label lblOptionsPath = new Label();

        public ClientSettings()
        {
            InitializeComponent();
            InitializeLegacySettings();
        }

        internal ClientSettings(GameProfile gameProfile)
        {
            this.gameProfile = gameProfile;
            InitializeComponent();
            BuildProfileOptionsUi();
            LoadProfileOptions();
        }

        private void InitializeLegacySettings()
        {
            
            
            

            int RamAllowed = 3;
            int ViewDistance = 8;
            bool JREUpdated = false;
            bool JVMOptimiwed = false;
            bool LibrariesUpdated = false;

            if (File.Exists(Settings.Actual_Path + @"\settings.ini"))
            {
                Settings.RamAllowed = int.Parse(iniFile.IniReadValueAndCreateKeyIfNull("SETTINGS", "RamAllowed", RamAllowed.ToString()));
                Settings.ViewDistance = int.Parse(iniFile.IniReadValueAndCreateKeyIfNull("SETTINGS", "ViewDistance", ViewDistance.ToString()));
                Settings.JREUpdated = bool.Parse(iniFile.IniReadValueAndCreateKeyIfNull("SETTINGS", "JREUpdated", JREUpdated.ToString()));
                Settings.JVMOptimized = bool.Parse(iniFile.IniReadValueAndCreateKeyIfNull("SETTINGS", "JVMOptimized", JVMOptimiwed.ToString()));
                Settings.LibrariesUpdated = bool.Parse(iniFile.IniReadValueAndCreateKeyIfNull("SETTINGS", "LibrariesUpdated", LibrariesUpdated.ToString()));
            }

            tb_ViewDistance.Value = Settings.ViewDistance;
            tb_RamAllowed.Value = Settings.RamAllowed;
            lbl_AllowedRam.Text = Settings.RamAllowed.ToString() + " GB";
            lbl_ViewDistance.Text = Settings.ViewDistance.ToString();

            if (tb_ViewDistance.Value == 13)
            {
                lbl_ViewDistance.Text = "(Default)";
            }
            else
            {
                lbl_ViewDistance.Text = "";
            }

            lbl_ViewDistanceDetails.Text = (Math.Floor(tb_ViewDistance.Value * 1.5f) * 10).ToString() + " tiles";



            InstalledRam = GetTotalMemoryInBytes();
            if (InstalledRam > 2)
            {
                tb_RamAllowed.Maximum = Convert.ToInt32(InstalledRam / (1024 * 1024 * 1024));
            }
            else
            {
                tb_RamAllowed.Maximum = 4;
            }


        }

        private void BuildProfileOptionsUi()
        {
            if (gameProfile == null)
            {
                return;
            }

            TopMost = false;
            Text = $"Options - {gameProfile.Name}";
            BackColor = Color.FromArgb(30, 32, 36);
            MinimumSize = new Size(900, 620);
            Size = new Size(980, 700);

            foreach (Control control in Controls)
            {
                control.Visible = false;
            }

            var title = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(18, 16),
                Size = new Size(520, 34),
                Text = $"Options.ini - {gameProfile.Name}"
            };
            Controls.Add(title);

            lblOptionsPath = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(166, 174, 186),
                Location = new Point(20, 54),
                Size = new Size(ClientSize.Width - 40, 22)
            };
            Controls.Add(lblOptionsPath);

            var searchLabel = new Label
            {
                AutoSize = true,
                ForeColor = Color.White,
                Location = new Point(20, 90),
                Text = "Filtre"
            };
            Controls.Add(searchLabel);

            tbxOptionsFilter = new TextBox
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(24, 26, 30),
                BorderStyle = BorderStyle.FixedSingle,
                ForeColor = Color.White,
                Location = new Point(68, 86),
                Size = new Size(ClientSize.Width - 88, 23)
            };
            tbxOptionsFilter.TextChanged += (sender, args) => ApplyOptionsFilter();
            Controls.Add(tbxOptionsFilter);

            dgvOptions = new DataGridView
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoGenerateColumns = false,
                BackgroundColor = Color.FromArgb(34, 37, 42),
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Color.FromArgb(55, 60, 68),
                Location = new Point(20, 124),
                MultiSelect = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Size = new Size(ClientSize.Width - 40, ClientSize.Height - 196)
            };
            dgvOptions.DefaultCellStyle.BackColor = Color.FromArgb(30, 32, 36);
            dgvOptions.DefaultCellStyle.ForeColor = Color.FromArgb(226, 230, 236);
            dgvOptions.DefaultCellStyle.SelectionBackColor = Color.FromArgb(82, 58, 62);
            dgvOptions.DefaultCellStyle.SelectionForeColor = Color.White;
            dgvOptions.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 49, 56);
            dgvOptions.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvOptions.EnableHeadersVisualStyles = false;
            dgvOptions.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(OptionEntry.Key),
                HeaderText = "Option",
                Width = 260
            });
            dgvOptions.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(OptionEntry.Value),
                HeaderText = "Valeur",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
            dgvOptions.DataSource = visibleOptionEntries;
            Controls.Add(dgvOptions);

            int buttonY = ClientSize.Height - 56;
            var btnAdd = CreateProfileOptionButton("Ajouter", 20, buttonY, 100);
            btnAdd.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnAdd.Click += (sender, args) => AddOptionEntry();
            Controls.Add(btnAdd);

            var btnDelete = CreateProfileOptionButton("Supprimer", 128, buttonY, 110);
            btnDelete.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnDelete.Click += (sender, args) => DeleteSelectedOptions();
            Controls.Add(btnDelete);

            var btnImport = CreateProfileOptionButton("Importer global", 246, buttonY, 140);
            btnImport.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnImport.Click += (sender, args) => ImportGlobalOptions();
            Controls.Add(btnImport);

            var btnOpen = CreateProfileOptionButton("Ouvrir", ClientSize.Width - 324, buttonY, 94);
            btnOpen.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnOpen.Click += (sender, args) => OpenOptionsFile();
            Controls.Add(btnOpen);

            var btnSave = CreateProfileOptionButton("Sauver", ClientSize.Width - 222, buttonY, 96);
            btnSave.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnSave.BackColor = Color.FromArgb(154, 38, 43);
            btnSave.Click += (sender, args) => SaveProfileOptions();
            Controls.Add(btnSave);

            var btnClose = CreateProfileOptionButton("Fermer", ClientSize.Width - 118, buttonY, 98);
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnClose.Click += (sender, args) => Close();
            Controls.Add(btnClose);
        }

        private static Button CreateProfileOptionButton(string text, int x, int y, int width)
        {
            var button = new Button
            {
                BackColor = Color.FromArgb(55, 60, 68),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                Location = new Point(x, y),
                Size = new Size(width, 34),
                Text = text,
                UseVisualStyleBackColor = false
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(86, 38, 43);
            return button;
        }

        private void LoadProfileOptions()
        {
            if (gameProfile == null)
            {
                return;
            }

            optionsIniPath = optionsIniService.EnsureProfileOptionsFile(gameProfile);
            lblOptionsPath.Text = optionsIniPath;
            optionEntries.Clear();
            optionEntries.AddRange(optionsIniService.Load(optionsIniPath));
            ApplyOptionsFilter();
        }

        private void ApplyOptionsFilter()
        {
            string filter = tbxOptionsFilter.Text.Trim();

            IEnumerable<OptionEntry> query = optionEntries;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                query = query.Where(entry =>
                    entry.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || entry.Value.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            visibleOptionEntries.RaiseListChangedEvents = false;
            visibleOptionEntries.Clear();
            foreach (OptionEntry entry in query)
            {
                visibleOptionEntries.Add(entry);
            }

            visibleOptionEntries.RaiseListChangedEvents = true;
            visibleOptionEntries.ResetBindings();
        }

        private void AddOptionEntry()
        {
            var entry = new OptionEntry { Key = "new.option", Value = string.Empty };
            optionEntries.Add(entry);
            ApplyOptionsFilter();
            SelectOptionEntry(entry);
        }

        private void SelectOptionEntry(OptionEntry entry)
        {
            for (int i = 0; i < dgvOptions.Rows.Count; i++)
            {
                if (ReferenceEquals(dgvOptions.Rows[i].DataBoundItem, entry))
                {
                    dgvOptions.ClearSelection();
                    dgvOptions.Rows[i].Selected = true;
                    dgvOptions.CurrentCell = dgvOptions.Rows[i].Cells[0];
                    return;
                }
            }
        }

        private void DeleteSelectedOptions()
        {
            var selectedEntries = dgvOptions.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(row => row.DataBoundItem)
                .OfType<OptionEntry>()
                .ToList();

            foreach (OptionEntry entry in selectedEntries)
            {
                optionEntries.Remove(entry);
            }

            ApplyOptionsFilter();
        }

        private void ImportGlobalOptions()
        {
            if (gameProfile == null)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                "Remplacer le options.ini de ce profil par le fichier global ?",
                "Importer options.ini",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            try
            {
                optionsIniService.ImportGlobalOptions(gameProfile);
                LoadProfileOptions();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Import options.ini", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveProfileOptions()
        {
            dgvOptions.EndEdit();

            var duplicates = optionEntries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                .GroupBy(entry => entry.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            if (duplicates.Count > 0)
            {
                MessageBox.Show(
                    "Options en double : " + string.Join(", ", duplicates),
                    "options.ini",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            optionsIniService.Save(optionsIniPath, optionEntries);
            MessageBox.Show("options.ini sauvegarde.", "options.ini", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenOptionsFile()
        {
            if (!File.Exists(optionsIniPath))
            {
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = optionsIniPath,
                UseShellExecute = true
            });
        }

        private void btn_Close_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void tb_ViewDistance_Scroll(object sender, EventArgs e)
        {
            if (tb_ViewDistance.Value == 13)
            {
                lbl_ViewDistance.Text = "(Default)";
            }
            else
            {
                lbl_ViewDistance.Text = "";
            }

            lbl_ViewDistanceDetails.Text = (Math.Floor(tb_ViewDistance.Value * 1.5f) * 10).ToString() + " tiles";
        }

        static ulong GetTotalMemoryInBytes()
        {
            return new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory;
        }

        private void tb_RamAllowed_Scroll(object sender, EventArgs e)
        {
            lbl_AllowedRam.Text = tb_RamAllowed.Value.ToString() + " GB";
        }

        private void btn_ClientSave_Click(object sender, EventArgs e)
        {

            System.Net.WebClient wc = new System.Net.WebClient();

            if (rb_JVMoff.Checked)
            {
                Settings.JVMOptimized = false;
            }
            else
            {
                Settings.JVMOptimized = true;
            }

            if (rb_JREoff.Checked)
            {
                Settings.JREUpdated = false;
            }
            else
            {
                Settings.JREUpdated = true;

                string new_url = @"https://download.oracle.com/graalvm/20/latest/graalvm-jdk-20_windows-x64_bin.zip";
                wc.Headers.Add("user-agent", "PZLauncherDownloader");
                wc.DownloadFile(new_url, Settings.Actual_Path + @"\JREUpdate.zip");

            }

            if (rb_LIBoff.Checked)
            {
                Settings.LibrariesUpdated = false;
            }
            else
            {
                Settings.LibrariesUpdated = true;
            }

            string ViewDistanceClassFile = Settings.PZ_Path + @"\zombie\iso\IsoChunkMap.class";
            string ViewDistanceClassFile_NackUp = Settings.PZ_Path + @"\zombie\iso\IsoChunkMap.class.pzlauncher";
            if (File.Exists(ViewDistanceClassFile_NackUp))
            {
                if (FileChecksum.Get_Sha1(ViewDistanceClassFile_NackUp) == "6F0D48D8D51F44E42987703BAD8344964761284C")
                {
                    if (tb_ViewDistance.Value < 13)
                    {
                        if (Patch.ApplyPatch(ViewDistanceClassFile, ViewDistanceClassFile + ".pzlauncher", Settings.Actual_Path + @"\patch\" + tb_ViewDistance.Value.ToString() + "_patch.bin"))
                        {
                            MessageBox.Show("Game Patched !");
                        }
                    }
                    else
                    {
                        File.Copy(ViewDistanceClassFile_NackUp, ViewDistanceClassFile, true);
                    }
                }
            }
            else if (!File.Exists(ViewDistanceClassFile_NackUp) && File.Exists(ViewDistanceClassFile))
            {
                if (FileChecksum.Get_Sha1(ViewDistanceClassFile) == "6F0D48D8D51F44E42987703BAD8344964761284C")
                {
                    if (tb_ViewDistance.Value < 13)
                    {
                        File.Copy(ViewDistanceClassFile, ViewDistanceClassFile + ".pzlauncher", true);
                        if (Patch.ApplyPatch(ViewDistanceClassFile, ViewDistanceClassFile + ".pzlauncher", Settings.Actual_Path + @"\patch\" + tb_ViewDistance.Value.ToString() + "_patch.bin"))
                        {
                            MessageBox.Show("Game Patched !");
                        }
                    }
                }
            }
            Settings.RamAllowed = tb_RamAllowed.Value;
            Settings.ViewDistance = tb_ViewDistance.Value;



            iniFile.IniWriteValue("SETTINGS", "RamAllowed", Settings.RamAllowed.ToString());
            iniFile.IniWriteValue("SETTINGS", "ViewDistance", Settings.ViewDistance.ToString());
            iniFile.IniWriteValue("SETTINGS", "JREUpdated", Settings.JREUpdated.ToString());
            iniFile.IniWriteValue("SETTINGS", "JVMOptimized", Settings.JVMOptimized.ToString());
            iniFile.IniWriteValue("SETTINGS", "LibrariesUpdated", Settings.LibrariesUpdated.ToString());


            this.Close();
        }

        private void btn_Cancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void label13_Click(object sender, EventArgs e)
        {

        }

        private void ClientSettings_Load(object sender, EventArgs e)
        {

        }
    }




}
