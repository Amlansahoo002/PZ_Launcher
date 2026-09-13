using PZLauncher.Desktop;
namespace PZLauncher;
internal static class Program
{
    public static bool OpenDisclaimer { get; set; }
    [STAThread]
    static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--steam-browser")
        {
            Environment.ExitCode = SteamServerBrowser.Helper(args);
            return;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LauncherStorage.Log(e.ExceptionObject.ToString() ?? "Erreur inconnue");
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetColorMode(SystemColorMode.Dark);
            string? ftpFixture = args.FirstOrDefault(a => a.StartsWith("--verify-ftp="))?["--verify-ftp=".Length..];
            if (ftpFixture != null)
            {
                int port = int.Parse(args.First(a => a.StartsWith("--ftp-port="))["--ftp-port=".Length..]);
                Environment.ExitCode = FtpWorldVerification.Network(Path.GetFullPath(ftpFixture), port); return;
            }
            if (args.Contains("--verify-configuration-booleans", StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = ConfigurationBooleanVerification.Smoke(); return;
            }
            string? serverJavaSmoke = args.FirstOrDefault(a => a.StartsWith("--smoke-server-java="))?["--smoke-server-java=".Length..];
            if (serverJavaSmoke != null)
            {
                string output = args.First(a => a.StartsWith("--output="))["--output=".Length..];
                string jdk = args.First(a => a.StartsWith("--jdk="))["--jdk=".Length..];
                Environment.ExitCode = RuntimeControlVerification.Run(Path.GetFullPath(serverJavaSmoke), Path.GetFullPath(output), Path.GetFullPath(jdk)); return;
            }
            string? packVerification = args.FirstOrDefault(a => a.StartsWith("--verify-runtime-pack="))?["--verify-runtime-pack=".Length..];
            if (packVerification != null)
            {
                Environment.ExitCode = RuntimePackVerification.Verify(Path.GetFullPath(packVerification)); return;
            }
            string? packSmoke = args.FirstOrDefault(a => a.StartsWith("--smoke-runtime-pack="))?["--smoke-runtime-pack=".Length..];
            if (packSmoke != null)
            {
                string output = args.First(a => a.StartsWith("--output="))["--output=".Length..];
                Environment.ExitCode = RuntimePackVerification.Smoke(Path.GetFullPath(packSmoke), Path.GetFullPath(output)); return;
            }
            if (args.Contains("--verify-server-statistics", StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = ServerStatisticsVerification.Smoke();
                return;
            }
            if (args.Contains("--verify-server-probe", StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = ServerProbeVerification.Smoke(args.Contains("--steam"));
                return;
            }
            if (args.Contains("--verify-loader-downloads", StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = LoaderDownloadVerification.Smoke();
                return;
            }
            if (args.Contains("--verify-browser-stream", StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = BrowserWindowVerification.Smoke();
                return;
            }
            if (args.Contains("--verify-steamcmd", StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = WorkshopVerification.Smoke();
                return;
            }
            if (args.Contains("--verify", StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = LauncherVerification.Run();
                return;
            }
            string? renderDirectory = args.FirstOrDefault(a => a.StartsWith("--render-ui="))?["--render-ui=".Length..];
            string? verifyLaunch = args.FirstOrDefault(a => a.StartsWith("--verify-launch="))?["--verify-launch=".Length..];
            string? verifyCache = args.FirstOrDefault(a => a.StartsWith("--verify-cache="))?["--verify-cache=".Length..];
            Application.Run(new LauncherWindow(renderDirectory, verifyLaunchDirectory: verifyLaunch, expectedLaunchCache: verifyCache));
        }
        catch (Exception ex) { ReportError(ex); }
    }
    private static void ReportError(Exception ex)
    {
        LauncherStorage.Log(ex.ToString());
        MessageBox.Show(T("error.app", ex.Message, LauncherStorage.LogPath),
            "PZLauncher", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
