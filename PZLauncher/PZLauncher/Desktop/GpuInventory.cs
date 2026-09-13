using System.Runtime.InteropServices;

namespace PZLauncher.Desktop;

internal sealed record GpuInfo(string Name, long DedicatedMb, long SharedMb, bool Integrated);
internal static class GpuInventory
{
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name;
        public uint Vendor, Device, Subsystem, Revision;
        public nuint DedicatedVideo, DedicatedSystem, SharedSystem;
        public long Luid;
        public uint Flags;
    }
    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumerateAdapter(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReadDescription(IntPtr self, out AdapterDescription description);
    private static T Method<T>(IntPtr pointer, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), slot * IntPtr.Size));
    internal static IReadOnlyList<GpuInfo> Read()
    {
        var result = new List<GpuInfo>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        try
        {
            var enumerate = Method<EnumerateAdapter>(factory, 12);
            for (uint index = 0; index < 32; index++)
            {
                int hr = enumerate(factory, index, out var adapter);
                if (hr == unchecked((int)0x887A0002)) break; 
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    Marshal.ThrowExceptionForHR(Method<ReadDescription>(adapter, 10)(adapter, out var info));
                    if ((info.Flags & 2) != 0) continue; 
                    long dedicated = (long)(info.DedicatedVideo / 1048576);
                    bool integrated = dedicated < 1024 || System.Text.RegularExpressions.Regex.IsMatch(info.Name, "Intel.*(UHD|HD Graphics|Iris)|Radeon\\(TM\\) Graphics", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    result.Add(new(info.Name.Trim(), dedicated, (long)(info.SharedSystem / 1048576), integrated));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return result;
    }
}
