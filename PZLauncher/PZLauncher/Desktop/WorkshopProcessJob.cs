using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PZLauncher.Desktop;


internal sealed class WorkshopProcessJob : IDisposable
{
    private IntPtr handle;
    internal WorkshopProcessJob(Process process)
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new Win32Exception();
        var limits = new ExtendedLimit { Basic = new BasicLimit { Flags = 0x2000 } }; 
        int size = Marshal.SizeOf<ExtendedLimit>();
        IntPtr data = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, data, false);
            if (!SetInformationJobObject(handle, 9, data, (uint)size) || !AssignProcessToJobObject(handle, process.Handle))
                throw new Win32Exception();
        }
        catch
        {
            Dispose();
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            throw;
        }
        finally { Marshal.FreeHGlobal(data); }
    }
    public void Dispose()
    {
        IntPtr old = Interlocked.Exchange(ref handle, IntPtr.Zero);
        if (old != IntPtr.Zero) CloseHandle(old);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public UIntPtr Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
