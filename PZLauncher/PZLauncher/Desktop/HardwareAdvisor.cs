using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using System.Management;

namespace PZLauncher.Desktop;

internal sealed record HardwareSnapshot(long TotalMb, long AvailableMb, int Threads, string Cpu, string JavaVersion, string DefaultCollector)
{
    public int Cores { get; init; }
    public int JavaStackKb { get; init; } = 1024;
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
}
internal sealed record JvmSuggestion(int XmsMb, int XmxMb, int StackKb = 1024, int PauseTargetMs = 200, string Collector = "G1", bool StringDeduplication = false, int ReservedMb = 4096);
internal static class HardwareAdvisor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, ExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    internal static HardwareSnapshot Detect(string installation)
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new IOException(T("jvm.detectFailed"));
        string cpu = "";
        using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            cpu = (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
        string java = "";
        string release = Path.Combine(installation, "jre64", "release");
        if (File.Exists(release))
            java = File.ReadLines(release).FirstOrDefault(l => l.StartsWith("JAVA_VERSION="))?.Split('=', 2)[1].Trim('"') ?? "";
        string collector = "";
        string config = Path.Combine(installation, "ProjectZomboid64.json");
        try
        {
            if (File.Exists(config))
            {
            using var json = JsonDocument.Parse(File.ReadAllText(config));
            
            var args = new List<string>();
            void ReadArgs(JsonElement element)
            {
                if (element.ValueKind != JsonValueKind.Object) return;
                foreach (var item in element.EnumerateObject())
                    if (item.Name == "vmArgs" && item.Value.ValueKind == JsonValueKind.Array)
                        args.AddRange(item.Value.EnumerateArray().Select(v => v.GetString() ?? ""));
                    else if (item.Name == "windows")
                    {
                        var platform = item.Value.EnumerateObject().Where(p => Version.TryParse(p.Name, out var v) && v <= Environment.OSVersion.Version)
                            .OrderByDescending(p => Version.Parse(p.Name)).FirstOrDefault();
                        if (platform.Value.ValueKind == JsonValueKind.Object) ReadArgs(platform.Value);
                    }
            }
            ReadArgs(json.RootElement);
            collector = args.LastOrDefault(a => a.StartsWith("-XX:+Use") && a.EndsWith("GC"))?.Replace("-XX:+Use", "") ?? "JVM";
            if (collector == "G1GC") collector = "G1";
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException) { LauncherStorage.Log("Optional game JVM reference: " + ex.Message); }
        int cores = 0;
        try
        {
            using var search = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
            search.Options.Timeout = TimeSpan.FromSeconds(2);
            using var rows = search.Get();
            foreach (ManagementObject row in rows) { using (row) cores += Convert.ToInt32(row["NumberOfCores"]); }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException) { LauncherStorage.Log("CPU topology: " + ex.Message); }
        IReadOnlyList<GpuInfo> gpus = [];
        try { gpus = GpuInventory.Read(); }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException) { LauncherStorage.Log("GPU detection: " + ex.Message); }
        return new((long)(memory.TotalPhysical / 1048576), (long)(memory.AvailablePhysical / 1048576), Environment.ProcessorCount, cpu, java, collector)
        { Cores = cores, Gpus = gpus, JavaStackKb = ReadJavaStackKb(Path.Combine(installation, "jre64", "bin", "java.exe")) };
    }
    internal static JvmSuggestion Suggest(HardwareSnapshot hardware)
    {
        
        int desired = hardware.TotalMb >= 30 * 1024 ? 8192 : hardware.TotalMb >= 15 * 1024 ? 6144 : hardware.TotalMb >= 7 * 1024 ? 4096 : 2048;
        int osReserve = (int)Math.Clamp(hardware.TotalMb / 8, 2048, 8192);
        bool sharedGraphics = hardware.Gpus.Count > 0 && hardware.Gpus.All(g => g.Integrated);
        int nativeReserve = sharedGraphics ? (int)Math.Clamp(hardware.TotalMb / 8, 2048, 4096)
            : hardware.Gpus.Count == 0 || hardware.Gpus.Max(g => g.DedicatedMb) < 4096 ? 3072 : 2048;
        long budget = Math.Min(hardware.TotalMb / 2, Math.Max(1024, hardware.AvailableMb - Math.Max(1024, osReserve / 2) - nativeReserve));
        if (hardware.TotalMb <= 0) budget = desired = 2048;
        int maximum = Math.Max(1024, (int)(Math.Min(desired, budget) / 512) * 512);
        bool modernJava = int.TryParse(hardware.JavaVersion.Split('.', '-')[0], out int javaMajor) && javaMajor >= 21;
        bool concurrentBudget = hardware.Threads >= 8 && hardware.TotalMb >= 15 * 1024 && maximum >= 4096;
        string collector = modernJava && concurrentBudget ? "ZGC" : "G1";
        
        int pause = 200;
        return new(Math.Min(2048, Math.Max(512, maximum / 4)), maximum, hardware.JavaStackKb, pause, collector, false, osReserve + nativeReserve);
    }
    internal static int ReadJavaStackKb(string executable)
    {
        
        try
        {
            using var reader = new BinaryReader(File.OpenRead(executable));
            if (reader.ReadUInt16() != 0x5A4D) return 1024;
            reader.BaseStream.Position = 0x3c; int pe = reader.ReadInt32();
            if (pe < 0 || pe > reader.BaseStream.Length - 120) return 1024;
            reader.BaseStream.Position = pe; if (reader.ReadUInt32() != 0x4550) return 1024;
            reader.BaseStream.Position = pe + 24; ushort magic = reader.ReadUInt16();
            if (magic is not (0x20b or 0x10b)) return 1024;
            reader.BaseStream.Position = pe + 24 + 72;
            ulong reserve = magic == 0x20b ? reader.ReadUInt64() : reader.ReadUInt32();
            return (int)Math.Clamp(reserve / 1024, 1024UL, 16384UL);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 1024; }
    }
    internal static bool FillUnset(ProfilePreferences profile, JvmSuggestion suggestion)
    {
        bool changed = false;
        if (profile.MemoryMb == 0 && profile.InitialMemoryMb == 0 && profile.Collector == "Jeu") profile.AutomaticJvm = true;
        if (profile.MemoryMb == 0) { profile.MemoryMb = Math.Max(profile.InitialMemoryMb, suggestion.XmxMb); changed = true; }
        if (profile.InitialMemoryMb == 0) { profile.InitialMemoryMb = Math.Min(profile.MemoryMb, suggestion.XmsMb); changed = true; }
        if (profile.StackKb == 0) { profile.StackKb = suggestion.StackKb; changed = true; }
        if (profile.PauseTargetMs == 0) { profile.PauseTargetMs = suggestion.PauseTargetMs; changed = true; }
        if (profile.Collector == "Jeu") { profile.Collector = suggestion.Collector; changed = true; }
        return changed;
    }
    internal static void ConfigureServer(ServerPreset profile, JvmSuggestion suggestion, bool overwrite = false)
    {
        if (overwrite || profile.XmxMb == 0) profile.XmxMb = suggestion.XmxMb;
        if (overwrite || profile.XmsMb == 0) profile.XmsMb = Math.Min(profile.XmxMb, suggestion.XmsMb);
        if (overwrite || profile.StackKb == 0) profile.StackKb = suggestion.StackKb;
        if (overwrite || profile.PauseTargetMs == 0) profile.PauseTargetMs = suggestion.PauseTargetMs;
        if (overwrite) { profile.Collector = suggestion.Collector; profile.StringDeduplication = suggestion.StringDeduplication; }
    }
}
