using Microsoft.Win32;
using PZLauncher.utils;
using System.Runtime.InteropServices;

namespace PZLauncher
{
    public partial class Form1 : Form
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
        IniFile iniFile = new IniFile(Settings.Actual_Path + @"\settings.ini");
        public Form1()
        {
            InitializeComponent();
            Settings.PZ_Path = iniFile.IniReadValueAndCreateKeyIfNull("PROJECTZOMBOID", "Path", Settings.Actual_Path);
            Settings.Disclaimer_Agreed = iniFile.IniReadValueAndCreateKeyIfNull("DISCLAIMER", "Agreed", "false");
            Settings.PZ_Steam_Version = iniFile.IniReadValueAndCreateKeyIfNull("STEAM", "Version", "false");
            Settings.Steam_Path = iniFile.IniReadValueAndCreateKeyIfNull("STEAM", "Path", Settings.Actual_Path);
            IntPtr handle = CreateRoundRectRgn(0, 0, Width, Height, 20, 20);
            if (handle == IntPtr.Zero); 
            Region = System.Drawing.Region.FromHrgn(handle);
            Shown += Form1_Shown;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (Settings.Disclaimer_Agreed.ToLower() != "true")
            {
                cbx_Disclaimer_Agree.Enabled = true;
                cbx_Disclaimer_Disagree.Enabled = true;
                cbx_Disclaimer_Agree.Checked = false;
                cbx_Disclaimer_Disagree.Checked = true;
                btn_PZPath.Enabled = false;
            }
            else
            {
                cbx_Disclaimer_Agree.Enabled = false;
                cbx_Disclaimer_Disagree.Enabled = false;
                cbx_Disclaimer_Agree.Checked = true;
                cbx_Disclaimer_Disagree.Checked = false;
                btn_PZPath.Enabled = true;
            }
        }
        private void Form1_Shown(object sender, EventArgs e)
        {
            if (Directory.Exists(Settings.PZ_Path) &&
                Settings.Disclaimer_Agreed.ToLower() == "true" &&
                File.Exists(Settings.PZ_Path + @"\ProjectZomboid64.exe"))
            {
                Program.OpenDisclaimer = true;
                Close(); 
            }
        }
        private void cbx_Disclaimer_Agree_CheckedChanged(object sender, EventArgs e)
        {
            if (cbx_Disclaimer_Agree.Checked == true)
            {
                cbx_Disclaimer_Disagree.Checked = false;
                btn_PZPath.Enabled = true;
            }
        }

        private void cbx_Disclaimer_Disagree_CheckedChanged(object sender, EventArgs e)
        {
            if (cbx_Disclaimer_Disagree.Checked == true)
            {
                cbx_Disclaimer_Agree.Checked = false;
                btn_PZPath.Enabled = false;
            }
        }

        private void btn_PZPath_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(Settings.PZ_Path) && File.Exists(Settings.PZ_Path + @"\ProjectZomboid64.exe"))
            {
                iniFile.IniWriteValue("DISCLAIMER", "Agreed", "true");
                Program.OpenDisclaimer = true;
                Close();
            }
            else
            {
                string path = @"";
                RegistryKey regKey = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam");
                if (regKey != null)
                {
                    object val = regKey.GetValue("SteamPath");
                    iniFile.IniWriteValue("STEAM", "Path", val.ToString());
                    path = val.ToString() + @"\steamapps\common\ProjectZomboid\";
                    if (File.Exists(path + "ProjectZomboid64.exe"))
                    {
                        iniFile.IniWriteValue("PROJECTZOMBOID", "Path", path);
                    }
                    else
                    {
                        OpenFileDialog res = new OpenFileDialog();

                        
                        res.Filter = "Project Zomboid Executable|ProjectZomboid64.exe;ProjectZomboid64.exe";

                        
                        if (res.ShowDialog() == DialogResult.OK)
                        {
                            
                            String filePath = res.FileName;
                            iniFile.IniWriteValue("PROJECTZOMBOID", "Path", Path.GetDirectoryName(filePath));
                            Settings.PZ_Path = Path.GetDirectoryName(filePath);
                            if (Directory.Exists(Settings.PZ_Path) && File.Exists(Settings.PZ_Path + @"\ProjectZomboid64.exe"))
                            {
                                iniFile.IniWriteValue("DISCLAIMER", "Agreed", "true");
                                Program.OpenDisclaimer = true;
                                Close();
                            }
                        }
                    }
                }
                


                iniFile.IniWriteValue("DISCLAIMER", "Agreed", "true");

            }


        }



        private void btn_Close_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}