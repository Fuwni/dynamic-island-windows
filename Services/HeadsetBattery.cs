using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DynamicIsland.Services;

/// <summary>
/// Battery of Logitech wireless headsets / mice (G435, G535, G733, G Pro…) read over the receiver's
/// HID++ 2.0 channel - the same protocol G HUB uses. Read-only queries.
/// </summary>
public static class HeadsetBattery
{
    public sealed record Reading(string Device, int Percent, bool Charging, string Method);

    private const ushort LogitechVid = 0x046D;

    /// <summary>Is a Logitech device with this USB product id plugged in.</summary>
    public static bool HasDevice(ushort pid) =>
        HidPaths(LogitechVid).Any(p => p.Contains($"pid_{pid:x4}", StringComparison.OrdinalIgnoreCase));

    public static Reading? Read(string deviceName = "Logitech")
    {
        foreach (var path in HidPaths(LogitechVid))
        {
            try
            {
                using var h = Open(path, out var caps);
                if (h == null || caps.OutputReportByteLength != 20) continue; // need HID++ long reports
                using var fs = new FileStream(h, FileAccess.ReadWrite, 20, isAsync: true);
                foreach (byte dev in new byte[] { 0xFF, 0x01, 0x02 })
                {
                    var r = Probe(fs, dev);
                    if (r != null) return r with { Device = deviceName };
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Logitech G HUB keeps battery levels for every device (incl. G435) and serves them on a local websocket.
    /// Works only when G HUB is installed and running.
    /// </summary>
    public static async Task<List<Reading>> ReadFromGHubAsync()
    {
        var result = new List<Reading>();
        using var ws = new System.Net.WebSockets.ClientWebSocket();
        ws.Options.AddSubProtocol("json");
        ws.Options.SetRequestHeader("Origin", "file://");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await ws.ConnectAsync(new Uri("ws://localhost:9010"), cts.Token);

            async Task<System.Text.Json.JsonElement?> Get(string path)
            {
                var req = System.Text.Json.JsonSerializer.Serialize(new { msgId = "", verb = "GET", path });
                await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(req), System.Net.WebSockets.WebSocketMessageType.Text, true, cts.Token);
                var buf = new byte[65536];
                for (int i = 0; i < 10; i++)
                {
                    var ms = new MemoryStream();
                    System.Net.WebSockets.WebSocketReceiveResult r;
                    do { r = await ws.ReceiveAsync(buf, cts.Token); ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
                    using var doc = System.Text.Json.JsonDocument.Parse(ms.ToArray());
                    if (doc.RootElement.TryGetProperty("path", out var p) && p.GetString() == path)
                        // answered: with data, or "NO_SUCH_PATH" (then don't wait for more)
                        return doc.RootElement.TryGetProperty("payload", out var payload) ? payload.Clone() : null;
                }
                return null;
            }

            var devices = await Get("/devices/list");
            if (devices?.TryGetProperty("deviceInfos", out var infos) == true)
            {
                foreach (var d in infos.EnumerateArray())
                {
                    // G HUB says itself which devices can report a battery (the G435, for example, can't)
                    if (d.TryGetProperty("capabilities", out var caps) &&
                        caps.TryGetProperty("hasBatteryStatus", out var hb) && hb.ValueKind == System.Text.Json.JsonValueKind.False)
                        continue;
                    var id = d.GetProperty("id").GetString();
                    var name = d.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? id : id;
                    var st = await Get($"/battery/{id}/state");
                    if (st?.TryGetProperty("percentage", out var pct) == true)
                        result.Add(new Reading(name ?? "", (int)pct.GetDouble(),
                            st.Value.TryGetProperty("charging", out var ch) && ch.GetBoolean(), "G HUB"));
                }
            }
        }
        catch { }
        return result;
    }

    private static Reading? Probe(FileStream fs, byte dev)
    {
        // unified battery (newer devices)
        if (Feature(fs, dev, 0x1004) is byte ub and > 0 && Call(fs, dev, ub, 1) is byte[] s && s[4] is > 0 and <= 100)
            return new Reading("", s[4], s[6] is 1 or 2 or 3, "HID++ 0x1004");

        // battery level status
        if (Feature(fs, dev, 0x1000) is byte bl and > 0 && Call(fs, dev, bl, 0) is byte[] b && b[4] is > 0 and <= 100)
            return new Reading("", b[4], b[6] is >= 1 and <= 4, "HID++ 0x1000");

        // battery voltage / ADC measurement (most Logitech headsets)
        foreach (ushort f in new ushort[] { 0x1001, 0x1F20 })
        {
            if (Feature(fs, dev, f) is byte idx and > 0 && Call(fs, dev, idx, 0) is byte[] v)
            {
                int mv = (v[4] << 8) | v[5];
                if (mv is > 3000 and < 4500)
                    return new Reading("", VoltageToPercent(mv), (v[6] & 0x03) != 0 && f == 0x1F20 || (v[6] & 0x80) != 0, $"HID++ 0x{f:X4} {mv} mV");
            }
        }
        return null;
    }

    private static int VoltageToPercent(int mv)
    {
        (int mv, int pct)[] curve =
        [
            (4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60),
            (3811, 50), (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3462, 0),
        ];
        if (mv >= curve[0].mv) return 100;
        for (int i = 1; i < curve.Length; i++)
        {
            if (mv >= curve[i].mv)
            {
                var (hi, lo) = (curve[i - 1], curve[i]);
                return (int)Math.Round(lo.pct + (double)(mv - lo.mv) / (hi.mv - lo.mv) * (hi.pct - lo.pct));
            }
        }
        return 0;
    }

    private const byte SwId = 0x0A;

    private static byte? Feature(FileStream fs, byte dev, ushort feature)
    {
        var r = Call(fs, dev, 0x00, 0, (byte)(feature >> 8), (byte)(feature & 0xFF));
        return r?[4];
    }

    private static byte[]? Call(FileStream fs, byte dev, byte featIdx, byte func, params byte[] args)
    {
        var req = new byte[20];
        req[0] = 0x11;
        req[1] = dev;
        req[2] = featIdx;
        req[3] = (byte)((func << 4) | SwId);
        Array.Copy(args, 0, req, 4, Math.Min(args.Length, 16));
        fs.Write(req, 0, req.Length);
        fs.Flush();

        var deadline = DateTime.Now.AddMilliseconds(700);
        var buf = new byte[20];
        while (DateTime.Now < deadline)
        {
            var read = fs.ReadAsync(buf, 0, buf.Length);
            if (!read.Wait(deadline - DateTime.Now) ) return null;
            if (read.Result < 7) continue;
            if (buf[0] is not (0x11 or 0x10) || buf[1] != dev) continue;
            if (buf[2] == 0xFF && buf[3] == featIdx) return null;                 // HID++ 2.0 error
            if (buf[0] == 0x10 && buf[2] == 0x8F) return null;                    // HID++ 1.0 error
            if (buf[2] == featIdx && buf[3] == req[3]) return (byte[])buf.Clone();
        }
        return null;
    }

    // ------------------------------------------------------------------ Win32 HID plumbing

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevs(ref Guid g, string? e, IntPtr p, int flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);

    private static IEnumerable<string> HidPaths(ushort vid)
    {
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x10 | 0x02); // PRESENT | DEVICEINTERFACE
        var list = new List<string>();
        try
        {
            var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref d); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                var buf = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, size, out _, IntPtr.Zero))
                    {
                        var path = Marshal.PtrToStringUni(buf + 4) ?? "";
                        if (path.Contains($"vid_{vid:x4}", StringComparison.OrdinalIgnoreCase)) list.Add(path);
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    private static SafeFileHandle? Open(string path, out HIDP_CAPS caps)
    {
        caps = default;
        const uint GENERIC_RW = 0xC0000000, SHARE_RW = 0x3, OPEN_EXISTING = 3, OVERLAPPED = 0x40000000;
        var h = CreateFile(path, GENERIC_RW, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, OVERLAPPED, IntPtr.Zero);
        if (h.IsInvalid) { h.Dispose(); return null; }
        if (HidD_GetPreparsedData(h, out var pd))
        {
            HidP_GetCaps(pd, out caps);
            HidD_FreePreparsedData(pd);
        }
        return h;
    }
}
