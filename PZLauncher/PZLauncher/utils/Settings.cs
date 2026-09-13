
namespace PZLauncher.utils
{
    internal class Settings
    {
        public static string Actual_Path = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        public static string Steam_Path { get; set; } = string.Empty;
        public static string PZ_Path { get; set; } = string.Empty;
        public static string Disclaimer_Agreed { get; set; } = string.Empty;
        public static string PZ_Steam_Version { get; set; } = string.Empty;
        public static int RamAllowed { get; set; }
        public static int ViewDistance { get; set; }
        public static bool JREUpdated { get; set; }
        public static bool JVMOptimized { get; set; }
        public static bool LibrariesUpdated { get; set; }
    }
}
