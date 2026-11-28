using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>Maps the capture device to the monitor name reported by Windows' active display paths.</summary>
public static class DisplayNames
{
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct Source { public Luid Adapter; public uint Id, Mode, Status; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct Target
    {
        public Luid Adapter;
        public uint Id, Mode, Technology, Rotation, Scaling;
        public Rational Refresh;
        public uint Scanline;
        public int Available;
        public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayPath { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] private struct ModeInfo { [FieldOffset(0)] public uint Type; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetName
    {
        public Header Header;
        public uint Flags, Technology;
        public ushort Manufacturer, Product;
        public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] pathArray, ref uint modes, [Out] ModeInfo[] modeArray, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetSourceName(ref SourceName name);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetTargetName(ref TargetName name);

    public static IReadOnlyDictionary<string, string> GetActive()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            const uint activePaths = 2;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (GetDisplayConfigBufferSizes(activePaths, out var pathCount, out var modeCount) != 0) break;
                var paths = new DisplayPath[pathCount];
                var modes = new ModeInfo[modeCount];
                int status = QueryDisplayConfig(activePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (status == 122) continue; // displays changed between sizing and querying
                if (status != 0) break;
                foreach (var path in paths.Take((int)pathCount))
                {
                    var source = new SourceName { Header = new Header { Type = 1, Size = (uint)Marshal.SizeOf<SourceName>(), Adapter = path.Source.Adapter, Id = path.Source.Id } };
                    var target = new TargetName { Header = new Header { Type = 2, Size = (uint)Marshal.SizeOf<TargetName>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
                    if (GetSourceName(ref source) != 0 || GetTargetName(ref target) != 0 || string.IsNullOrWhiteSpace(target.Name)) continue;
                    // Mirrored outputs share a capture source: keep all their names on that source's row.
                    result[source.Name] = result.TryGetValue(source.Name, out var previous) && previous != target.Name
                        ? previous + " / " + target.Name.Trim() : target.Name.Trim();
                }
                break;
            }
        }
        catch (Exception ex) { Log.Write("display names: " + ex.Message); }
        return result;
    }
}
