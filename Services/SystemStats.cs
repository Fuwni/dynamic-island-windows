using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>
/// CPU, GPU (all vendors, via Windows "GPU Engine" counters) and RAM load. Sample from a background thread.
/// One PDH query reads every GPU engine at once: asking each engine's counter separately made Windows
/// re-read its whole (huge) GPU table hundreds of times a second - that was most of the load in games.
/// </summary>
public sealed class SystemStats : IDisposable
{
    public sealed record Snapshot(double Cpu, double Gpu, double Ram, double RamUsedGb, double RamTotalGb, string GpuName);

    private IntPtr _query, _cpu, _gpu;
    private bool _opened;
    private string _gpuName = "";

    public Snapshot? Last { get; private set; }

    public Snapshot Sample()
    {
        if (!_opened)
        {
            _opened = true;
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) == 0)
            {
                if (PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out _cpu) != 0)
                    PdhAddEnglishCounter(_query, @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out _cpu);
                PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpu);
                PdhCollectQueryData(_query);   // rates need two samples: this is the first
            }
            _gpuName = GpuName();
        }

        double cpu = 0, gpu = 0;
        if (_query != IntPtr.Zero && PdhCollectQueryData(_query) == 0)
        {
            if (_cpu != IntPtr.Zero && PdhGetFormattedCounterValue(_cpu, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var v) == 0 && v.CStatus == 0)
                cpu = v.Value;
            if (_gpu != IntPtr.Zero) gpu = SumArray(_gpu);
        }

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        double total = mem.ullTotalPhys / 1073741824.0, avail = mem.ullAvailPhys / 1073741824.0;

        Last = new Snapshot(Math.Clamp(cpu, 0, 100), Math.Clamp(gpu, 0, 100), mem.dwMemoryLoad, total - avail, total, _gpuName);
        return Last;
    }

    /// <summary>Sum of all instances of a wildcard counter (every 3D engine of every process).</summary>
    private static double SumArray(IntPtr counter)
    {
        uint size = 0;
        int r = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out var count, IntPtr.Zero);
        if (r != PDH_MORE_DATA || size == 0) return 0;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buf) != 0) return 0;
            double sum = 0;
            int item = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (int i = 0; i < count; i++)
            {
                var it = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buf + i * item);
                if (it.Value.CStatus == 0) sum += it.Value.Value;
            }
            return sum;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static string GpuName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
            return key?.GetValue("DriverDesc") as string ?? "";
        }
        catch { return ""; }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    // ------------------------------------------------------------------ PDH

    private const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE { public uint CStatus; public double Value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM { public IntPtr Name; public PDH_FMT_COUNTERVALUE Value; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
}
