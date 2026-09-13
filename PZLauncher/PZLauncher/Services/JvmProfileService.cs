using Microsoft.VisualBasic.Devices;
using PZLauncher.Profiles;

namespace PZLauncher.Services
{
    internal sealed class JvmProfileService
    {
        private readonly string launcherRoot;

        public JvmProfileService(string launcherRoot)
        {
            this.launcherRoot = launcherRoot;
        }

        public IReadOnlyList<JvmProfile> GetDefaultProfiles()
        {
            var heap = ComputeAutoHeap();
            string agentPath = Path.Combine(launcherRoot, "agents", "pzpatcher.jar");

            return new[]
            {
                new JvmProfile
                {
                    Id = "vanilla-zgc",
                    Name = "Vanilla ZGC",
                    Description = "Proche du .bat officiel B42, avec -cachedir par profil.",
                    SteamEnabled = true,
                    ZNetLogEnabled = true,
                    UseZgc = true,
                    XmxMb = Math.Max(3072, heap.XmxMb / 2),
                    ConsoleLogSizeKb = 1024000
                },
                new JvmProfile
                {
                    Id = "opti-g1",
                    Name = "Opti G1",
                    Description = "Base de ton .bat optimise : G1, heap auto, znetlog coupe.",
                    SteamEnabled = false,
                    ZNetLogEnabled = false,
                    UseOptimizedG1 = true,
                    XmsMb = heap.XmsMb,
                    XmxMb = heap.XmxMb,
                    ConsoleLogSizeKb = 1024000
                },
                new JvmProfile
                {
                    Id = "debug-g1",
                    Name = "Debug / Radar",
                    Description = "Profil de test : G1, -debug, -novoip, logs isoles.",
                    SteamEnabled = false,
                    ZNetLogEnabled = false,
                    DebugEnabled = true,
                    NoVoip = true,
                    UseOptimizedG1 = true,
                    XmsMb = heap.XmsMb,
                    XmxMb = heap.XmxMb,
                    ConsoleLogSizeKb = 1024000
                },
                new JvmProfile
                {
                    Id = "patch-dev",
                    Name = "Patch dev",
                    Description = "Ajoute agents\\pzpatcher.jar si present, pour preparer ByteBuddy/javaagent.",
                    SteamEnabled = false,
                    ZNetLogEnabled = false,
                    DebugEnabled = true,
                    NoVoip = true,
                    UseOptimizedG1 = true,
                    XmsMb = heap.XmsMb,
                    XmxMb = heap.XmxMb,
                    ConsoleLogSizeKb = 1024000,
                    JavaAgentPath = File.Exists(agentPath) ? agentPath : string.Empty
                }
            };
        }

        private static (int XmsMb, int XmxMb) ComputeAutoHeap()
        {
            ulong totalMb;

            try
            {
                totalMb = new ComputerInfo().TotalPhysicalMemory / 1024 / 1024;
            }
            catch
            {
                totalMb = 8192;
            }

            if (totalMb >= 32768)
            {
                return (8192, 8192);
            }

            if (totalMb >= 16384)
            {
                return (4096, 6144);
            }

            return (2048, 4096);
        }
    }
}
