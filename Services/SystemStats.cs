using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>CPU, GPU (all vendors, via Windows "GPU Engine" counters) and RAM load. Sample from a background thread.</summary>
public sealed class SystemStats
{
    public sealed record Snapshot(double Cpu, double Gpu, double Ram, double RamUsedGb, double RamTotalGb, string GpuName);

    private PerformanceCounter? _cpu;
    private List<PerformanceCounter> _gpu = [];
    private DateTime _gpuListAt;
    private string _gpuName = "";

    public Snapshot? Last { get; private set; }

    public Snapshot Sample()
    {
        _cpu ??= TryCounter("Processor Information", "% Processor Utility", "_Total")
                 ?? TryCounter("Processor", "% Processor Time", "_Total");
        double cpu = Math.Clamp(Read(_cpu), 0, 100);

        if (DateTime.Now - _gpuListAt > TimeSpan.FromSeconds(10))
        {
            foreach (var c in _gpu) c.Dispose();
            _gpu = [];
            try
            {
                var cat = new PerformanceCounterCategory("GPU Engine");
                foreach (var inst in cat.GetInstanceNames().Where(i => i.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)))
                {
                    var c = TryCounter("GPU Engine", "Utilization Percentage", inst);
                    if (c != null) { c.NextValue(); _gpu.Add(c); }
                }
            }
            catch { }
            _gpuListAt = DateTime.Now;
            if (_gpuName.Length == 0) _gpuName = GpuName();
        }
        double gpu = Math.Clamp(_gpu.Sum(Read), 0, 100);

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        double total = mem.ullTotalPhys / 1073741824.0, avail = mem.ullAvailPhys / 1073741824.0;

        Last = new Snapshot(cpu, gpu, mem.dwMemoryLoad, total - avail, total, _gpuName);
        return Last;
    }

    private static double Read(PerformanceCounter? c)
    {
        try { return c?.NextValue() ?? 0; } catch { return 0; }
    }

    private static PerformanceCounter? TryCounter(string cat, string counter, string inst)
    {
        try
        {
            var c = new PerformanceCounter(cat, counter, inst, true);
            c.NextValue();
            return c;
        }
        catch { return null; }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
}
