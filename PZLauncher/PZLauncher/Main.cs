using System.Runtime.InteropServices;
using PZLauncher.utils;
using VCDiff.Includes;
using VCDiff.Encoders;
using VCDiff.Shared;
using VCDiff.Decoders;
using System.Diagnostics;
using PZLauncher.Profiles;
using PZLauncher.Services;

namespace PZLauncher
{
    public partial class Main : Form
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

        private string imageFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Zomboid\Screenshots";
        private Random random = new Random();
        private readonly ProfileService profileService;
        private readonly JvmProfileService jvmProfileService;
        private readonly GameLaunchService gameLaunchService;
        private readonly NewsService newsService;
        private IReadOnlyList<GameProfile> gameProfiles = Array.Empty<GameProfile>();
        private IReadOnlyList<JvmProfile> jvmProfiles = Array.Empty<JvmProfile>();
        private IReadOnlyList<NewsItem> newsItems = Array.Empty<NewsItem>();
        private Process? runningGameProcess;
        private Panel dashboardPanel = new Panel();
        private ComboBox cmbGameProfile = new ComboBox();
        private ComboBox cmbJvmProfile = new ComboBox();
        private Label lblProfileSummary = new Label();
        private Label lblInstallPath = new Label();
        private Label lblNewsStatus = new Label();
        private TextBox txtLaunchPreview = new TextBox();
        private TextBox txtStatus = new TextBox();
        private ListBox lstNews = new ListBox();
        private Button btnOpenProfile = new Button();
        private Button btnOpenLog = new Button();
        private Button btnRefreshProfiles = new Button();
        private Button btnOpenNews = new Button();
        private Button btnRefreshNews = new Button();

        public Main()
        {
            InitializeComponent();

            string launcherRoot = GetLauncherRoot();
            profileService = new ProfileService(Path.Combine(launcherRoot, "profiles"));
            jvmProfileService = new JvmProfileService(launcherRoot);
            gameLaunchService = new GameLaunchService();
            newsService = new NewsService();

            ImageChangeTimer.Stop();
            BuildDashboardUi();
            LoadLauncherProfiles();
            LoadPinnedNews();
            UpdateLaunchPreview();
            Shown += async (sender, args) => await RefreshNewsAsync();
        }

        private static string GetLauncherRoot()
        {
            return string.IsNullOrWhiteSpace(Settings.Actual_Path)
                ? AppContext.BaseDirectory
                : Settings.Actual_Path;
        }

        private void btn_Close_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            for (int i = 2; i < 13; i++)
            {
                using (FileStream output = new FileStream(Settings.Actual_Path + @"\patch\" + i + "_patch.bin", FileMode.Create, FileAccess.Write))
                using (FileStream dict = new FileStream(Settings.PZ_Path + @"\zombie\iso\IsoChunkMap.class", FileMode.Open, FileAccess.Read))
                using (FileStream target = new FileStream(Settings.Actual_Path + @"\bins\" + i + ".bin", FileMode.Open, FileAccess.Read))
                {
                    VCDiff.Encoders.VcEncoder coder = new VcEncoder(dict, target, output);
                    VCDiffResult result = coder.Encode(); 
                    if (result != VCDiffResult.SUCCESS)
                    {
                        
                    }
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            using (FileStream output = new FileStream(Settings.Actual_Path + @"\IsoChunkMap.class", FileMode.Create, FileAccess.Write))
            using (FileStream dict = new FileStream(Settings.Actual_Path + @"\original.bin", FileMode.Open, FileAccess.Read))
            using (FileStream target = new FileStream(Settings.Actual_Path + @"\patch.bin", FileMode.Open, FileAccess.Read))
            {
                VCDiff.Decoders.VcDecoder decoder = new VcDecoder(dict, target, output);

                long bytesWritten = 0;
                VCDiffResult result = decoder.Decode(out bytesWritten);

                if (result != VCDiffResult.SUCCESS)
                {
                    
                }

                
            }
        }

        private void btn_Client_Click(object sender, EventArgs e)
        {
            ClientSettings ClSettings = GetSelectedGameProfile() is GameProfile profile
                ? new ClientSettings(profile)
                : new ClientSettings();
            ClSettings.Show();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (runningGameProcess is { HasExited: false })
            {
                MessageBox.Show("Project Zomboid tourne deja avec ce launcher.", "Lancement", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            GameProfile? gameProfile = GetSelectedGameProfile();
            JvmProfile? jvmProfile = GetSelectedJvmProfile();

            if (gameProfile == null || jvmProfile == null)
            {
                MessageBox.Show("Selectionne un profil de jeu et un profil JVM.", "Lancement", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                LaunchPlan plan = gameLaunchService.CreateLaunchPlan(Settings.PZ_Path, gameProfile, jvmProfile);
                txtLaunchPreview.Text = plan.CommandLinePreview;
                AppendStatus($"Lancement: {gameProfile.Name} / {jvmProfile.Name}");
                AppendStatus($"Cache: {plan.CacheDirectory}");

                button4.Enabled = false;
                button4.Text = "EN COURS";

                runningGameProcess = gameLaunchService.Start(plan, OnGameOutput, OnGameOutput, OnGameExited);
            }
            catch (Exception ex)
            {
                button4.Enabled = true;
                button4.Text = "LANCER";
                AppendStatus($"Erreur de lancement: {ex.Message}");
                MessageBox.Show(ex.Message, "Erreur de lancement", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuildDashboardUi()
        {
            SuspendLayout();

            Color appBackground = Color.FromArgb(30, 32, 36);
            Color sidebarBackground = Color.FromArgb(22, 24, 28);
            Color surface = Color.FromArgb(39, 42, 48);
            Color surfaceAlt = Color.FromArgb(34, 37, 42);
            Color text = Color.FromArgb(236, 238, 242);
            Color muted = Color.FromArgb(158, 166, 178);
            Color accent = Color.FromArgb(154, 38, 43);

            Text = "PZLauncher";
            BackColor = appBackground;
            MinimumSize = new Size(900, 560);
            if (Width < 960 || Height < 620)
            {
                Size = new Size(Math.Max(Width, 960), Math.Max(Height, 620));
            }

            panel1.BackColor = sidebarBackground;
            foreach (Control control in panel1.Controls)
            {
                if (control is Button button)
                {
                    button.BackColor = Color.FromArgb(45, 49, 56);
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(86, 38, 43);
                }
            }

            label1.Text = "PZLauncher";
            label1.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
            label1.ForeColor = text;
            label1.Location = new Point(92, 13);
            label1.Size = new Size(420, 34);

            pbx_Main.Visible = false;
            lbl_pbxname.Visible = false;
            button1.Visible = false;
            button2.Visible = false;

            button4.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button4.BackColor = accent;
            button4.FlatAppearance.BorderSize = 0;
            button4.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            button4.ForeColor = Color.White;
            button4.ImageAlign = ContentAlignment.MiddleLeft;
            button4.Text = "LANCER";
            button4.TextAlign = ContentAlignment.MiddleRight;
            button4.Size = new Size(150, 44);
            button4.Location = new Point(ClientSize.Width - button4.Width - 18, 12);

            dashboardPanel = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = appBackground,
                Location = new Point(92, 62),
                Size = new Size(ClientSize.Width - 110, ClientSize.Height - 82)
            };

            Controls.Add(dashboardPanel);
            dashboardPanel.BringToFront();
            button4.BringToFront();
            label1.BringToFront();

            var leftPanel = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
                BackColor = surface,
                Location = new Point(0, 0),
                Size = new Size(340, dashboardPanel.Height)
            };
            dashboardPanel.Controls.Add(leftPanel);

            var rightPanel = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = surfaceAlt,
                Location = new Point(356, 0),
                Size = new Size(dashboardPanel.Width - 356, dashboardPanel.Height)
            };
            dashboardPanel.Controls.Add(rightPanel);

            AddLabel(leftPanel, "Profil de jeu", 18, 18, text, 11F, true);
            cmbGameProfile = CreateCombo(18, 46, 304);
            leftPanel.Controls.Add(cmbGameProfile);

            AddLabel(leftPanel, "Profil JVM", 18, 96, text, 11F, true);
            cmbJvmProfile = CreateCombo(18, 124, 304);
            leftPanel.Controls.Add(cmbJvmProfile);

            lblProfileSummary = new Label
            {
                ForeColor = muted,
                Location = new Point(18, 178),
                Size = new Size(304, 140),
                Font = new Font("Segoe UI", 9.2F),
                AutoEllipsis = true
            };
            leftPanel.Controls.Add(lblProfileSummary);

            lblInstallPath = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                ForeColor = muted,
                Location = new Point(18, leftPanel.Height - 104),
                Size = new Size(304, 46),
                Font = new Font("Segoe UI", 8.8F),
                AutoEllipsis = true
            };
            leftPanel.Controls.Add(lblInstallPath);

            btnOpenProfile = CreateActionButton("Cache", 18, leftPanel.Height - 48, 92);
            btnOpenProfile.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnOpenProfile.Click += (sender, args) => OpenSelectedProfileCache();
            leftPanel.Controls.Add(btnOpenProfile);

            btnOpenLog = CreateActionButton("Log", 122, leftPanel.Height - 48, 84);
            btnOpenLog.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnOpenLog.Click += (sender, args) => OpenSelectedProfileLog();
            leftPanel.Controls.Add(btnOpenLog);

            btnRefreshProfiles = CreateActionButton("Rafraichir", 218, leftPanel.Height - 48, 104);
            btnRefreshProfiles.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnRefreshProfiles.Click += (sender, args) =>
            {
                LoadLauncherProfiles();
                UpdateLaunchPreview();
                AppendStatus("Profils recharges.");
            };
            leftPanel.Controls.Add(btnRefreshProfiles);

            AddLabel(rightPanel, "Arguments de lancement", 18, 18, text, 11F, true);
            txtLaunchPreview = new TextBox
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(24, 26, 30),
                BorderStyle = BorderStyle.FixedSingle,
                ForeColor = Color.FromArgb(218, 222, 228),
                Font = new Font("Consolas", 9F),
                Location = new Point(18, 46),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Size = new Size(rightPanel.Width - 36, 104),
                WordWrap = true
            };
            rightPanel.Controls.Add(txtLaunchPreview);

            AddLabel(rightPanel, "News / Patchnotes", 18, 168, text, 11F, true);
            lblNewsStatus = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                ForeColor = muted,
                Location = new Point(rightPanel.Width - 210, 171),
                Size = new Size(192, 20),
                TextAlign = ContentAlignment.MiddleRight
            };
            rightPanel.Controls.Add(lblNewsStatus);

            lstNews = new ListBox
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(24, 26, 30),
                BorderStyle = BorderStyle.FixedSingle,
                ForeColor = Color.FromArgb(226, 230, 236),
                Font = new Font("Segoe UI", 9F),
                HorizontalScrollbar = true,
                IntegralHeight = false,
                Location = new Point(18, 196),
                Size = new Size(rightPanel.Width - 36, 74)
            };
            lstNews.DoubleClick += (sender, args) => OpenSelectedNewsItem();
            rightPanel.Controls.Add(lstNews);

            btnOpenNews = CreateActionButton("Ouvrir", 18, 280, 96);
            btnOpenNews.Click += (sender, args) => OpenSelectedNewsItem();
            rightPanel.Controls.Add(btnOpenNews);

            btnRefreshNews = CreateActionButton("Rafraichir", 124, 280, 110);
            btnRefreshNews.Click += async (sender, args) => await RefreshNewsAsync();
            rightPanel.Controls.Add(btnRefreshNews);

            AddLabel(rightPanel, "Sortie du process", 18, 330, text, 11F, true);
            txtStatus = new TextBox
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(19, 21, 25),
                BorderStyle = BorderStyle.FixedSingle,
                ForeColor = Color.FromArgb(206, 214, 223),
                Font = new Font("Consolas", 9F),
                Location = new Point(18, 358),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Size = new Size(rightPanel.Width - 36, rightPanel.Height - 376),
                WordWrap = false
            };
            rightPanel.Controls.Add(txtStatus);

            cmbGameProfile.SelectedIndexChanged += (sender, args) => UpdateLaunchPreview();
            cmbJvmProfile.SelectedIndexChanged += (sender, args) => UpdateLaunchPreview();

            ResumeLayout(false);
        }

        private ComboBox CreateCombo(int x, int y, int width)
        {
            return new ComboBox
            {
                BackColor = Color.FromArgb(24, 26, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                FormattingEnabled = true,
                Location = new Point(x, y),
                Size = new Size(width, 23)
            };
        }

        private Button CreateActionButton(string text, int x, int y, int width)
        {
            var button = new Button
            {
                BackColor = Color.FromArgb(55, 60, 68),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                Location = new Point(x, y),
                Size = new Size(width, 32),
                Text = text,
                UseVisualStyleBackColor = false
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(86, 38, 43);
            return button;
        }

        private static void AddLabel(Control parent, string text, int x, int y, Color color, float size, bool bold)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color,
                Location = new Point(x, y),
                Text = text
            });
        }

        private void LoadLauncherProfiles()
        {
            GameProfile? selectedGameProfile = GetSelectedGameProfile();
            JvmProfile? selectedJvmProfile = GetSelectedJvmProfile();

            gameProfiles = profileService.GetOrCreateDefaultProfiles();
            jvmProfiles = jvmProfileService.GetDefaultProfiles();

            cmbGameProfile.DataSource = null;
            cmbGameProfile.DataSource = gameProfiles.ToList();
            cmbJvmProfile.DataSource = null;
            cmbJvmProfile.DataSource = jvmProfiles.ToList();

            SelectById(cmbGameProfile, selectedGameProfile?.Id ?? "solo");
            SelectById(cmbJvmProfile, selectedJvmProfile?.Id ?? "opti-g1");
        }

        private static void SelectById(ComboBox comboBox, string id)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                object? item = comboBox.Items[i];
                string? itemId = item switch
                {
                    GameProfile gameProfile => gameProfile.Id,
                    JvmProfile jvmProfile => jvmProfile.Id,
                    _ => null
                };

                if (string.Equals(itemId, id, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private void UpdateLaunchPreview()
        {
            GameProfile? gameProfile = GetSelectedGameProfile();
            JvmProfile? jvmProfile = GetSelectedJvmProfile();

            if (gameProfile == null || jvmProfile == null)
            {
                return;
            }

            LaunchPlan plan = gameLaunchService.CreateLaunchPlan(Settings.PZ_Path, gameProfile, jvmProfile);
            int modCount = profileService.CountEnabledMods(gameProfile);

            lblProfileSummary.Text =
                $"{gameProfile.Description}{Environment.NewLine}{Environment.NewLine}" +
                $"Mods actifs: {modCount}{Environment.NewLine}" +
                $"Cache: {gameProfile.CacheDir}{Environment.NewLine}" +
                $"Log: {gameProfile.ConsoleLogPath}";

            lblInstallPath.Text = $"PZ: {Settings.PZ_Path}";
            txtLaunchPreview.Text = plan.CommandLinePreview;

            if (txtStatus.TextLength == 0)
            {
                AppendStatus("Pret.");
            }
        }

        private void LoadPinnedNews()
        {
            newsItems = newsService.GetPinnedItems();
            BindNewsItems("Sources locales");
        }

        private async Task RefreshNewsAsync()
        {
            if (IsDisposed)
            {
                return;
            }

            lblNewsStatus.Text = "Mise a jour...";
            btnRefreshNews.Enabled = false;

            try
            {
                newsItems = await newsService.RefreshPinnedItemsAsync(CancellationToken.None);
                BindNewsItems($"OK {DateTime.Now:HH:mm}");
            }
            finally
            {
                if (!IsDisposed)
                {
                    btnRefreshNews.Enabled = true;
                }
            }
        }

        private void BindNewsItems(string status)
        {
            lstNews.DataSource = null;
            lstNews.DataSource = newsItems.ToList();
            lblNewsStatus.Text = status;

            if (lstNews.Items.Count > 0)
            {
                lstNews.SelectedIndex = 0;
            }
        }

        private void OpenSelectedNewsItem()
        {
            if (lstNews.SelectedItem is not NewsItem item || string.IsNullOrWhiteSpace(item.Url))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = item.Url,
                UseShellExecute = true
            });
        }

        private GameProfile? GetSelectedGameProfile()
        {
            return cmbGameProfile.SelectedItem as GameProfile;
        }

        private JvmProfile? GetSelectedJvmProfile()
        {
            return cmbJvmProfile.SelectedItem as JvmProfile;
        }

        private void OpenSelectedProfileCache()
        {
            GameProfile? gameProfile = GetSelectedGameProfile();
            if (gameProfile == null)
            {
                return;
            }

            OpenPathInExplorer(gameProfile.CacheDir);
        }

        private void OpenSelectedProfileLog()
        {
            GameProfile? gameProfile = GetSelectedGameProfile();
            if (gameProfile == null)
            {
                return;
            }

            if (File.Exists(gameProfile.ConsoleLogPath))
            {
                Process.Start(new ProcessStartInfo(gameProfile.ConsoleLogPath) { UseShellExecute = true });
                return;
            }

            OpenPathInExplorer(gameProfile.CacheDir);
            MessageBox.Show("Aucun console.txt pour ce profil pour le moment.", "Log", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void OpenPathInExplorer(string path)
        {
            if (!Directory.Exists(path))
            {
                MessageBox.Show($"Dossier introuvable: {path}", "Dossier", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }

        private void OnGameOutput(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                AppendStatus(e.Data);
            }
        }

        private void OnGameExited(object? sender, EventArgs e)
        {
            if (sender is not Process process)
            {
                return;
            }

            int exitCode = -1;
            try
            {
                exitCode = process.ExitCode;
            }
            catch
            {
                
            }

            if (IsDisposed)
            {
                process.Dispose();
                return;
            }

            BeginInvoke(new Action(() =>
            {
                AppendStatus($"Jeu termine. Code retour: {exitCode}");
                button4.Enabled = true;
                button4.Text = "LANCER";

                if (ReferenceEquals(runningGameProcess, process))
                {
                    runningGameProcess = null;
                }

                process.Dispose();
            }));
        }

        private void AppendStatus(string message)
        {
            if (IsDisposed || !txtStatus.IsHandleCreated)
            {
                return;
            }

            if (txtStatus.InvokeRequired)
            {
                txtStatus.BeginInvoke(new Action(() => AppendStatus(message)));
                return;
            }

            txtStatus.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

            const int maxStatusLength = 30000;
            if (txtStatus.TextLength > maxStatusLength)
            {
                txtStatus.Text = txtStatus.Text.Substring(txtStatus.TextLength - maxStatusLength);
            }

            txtStatus.SelectionStart = txtStatus.TextLength;
            txtStatus.ScrollToCaret();
        }

        private void DisplayRandomImage()
        {
            try
            {
                string[] pngFiles = Directory.GetFiles(imageFolderPath, "*.png");
                if (pngFiles.Length > 0)
                {
                    int randomIndex = random.Next(0, pngFiles.Length);
                    string randomImagePath = pngFiles[randomIndex];

                    pbx_Main.Image = new System.Drawing.Bitmap(randomImagePath);
                    lbl_pbxname.Text = Path.GetFileNameWithoutExtension(randomImagePath);
                    GC.Collect();
                }
            }
            catch { }
        }

        private void ImageChangeTimer_Tick(object sender, EventArgs e)
        {
            DisplayRandomImage();
        }

        private void btn_PZ_Click(object sender, EventArgs e)
        {
            OpenPathInExplorer(Settings.PZ_Path);
        }

        private void button3_Click(object sender, EventArgs e)
        {

        }

        private void btn_Logs_Click(object sender, EventArgs e)
        {
            Form_LogAnalyzer LogAnalyzer = new Form_LogAnalyzer(GetSelectedGameProfile()?.ConsoleLogPath);
            LogAnalyzer.Show();
        }

        private void Main_Load(object sender, EventArgs e)
        {
            UpdateLaunchPreview();
        }
    }
}
