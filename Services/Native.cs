using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland.Services;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr h);
    [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    public const int WM_COMMAND = 0x0111;
    public const int WM_USER = 0x0400;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    /// <summary>Tests: a left click where the cursor is.</summary>
    public static void MouseClick() { mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder? name);

    /// <summary>The package family of a Store app's process ("" for an ordinary program).</summary>
    public static string PackageFamilyOf(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return "";
        try
        {
            uint len = 0;
            if (GetPackageFamilyName(h, ref len, null) != 122 || len == 0) return "";   // ERROR_INSUFFICIENT_BUFFER: it has one
            var sb = new StringBuilder((int)len);
            return GetPackageFamilyName(h, ref len, sb) == 0 ? sb.ToString() : "";
        }
        finally { CloseHandle(h); }
    }
    /// <summary>Is this key held down right now (whichever window has the keyboard)?</summary>
    public static bool KeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    public static IntPtr SendMessage(IntPtr h, int msg, int w, int l) => SendMessage(h, msg, (IntPtr)w, (IntPtr)l);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    /// <summary>Seconds since the user last touched the mouse or keyboard.</summary>
    public static double IdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0 : 0;
    }

    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    public static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string WindowText(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// True when a game / video / F11 browser covers the whole monitor that <paramref name="ours"/> is on.
    /// </summary>
    public static bool IsFullscreenAppActive(IntPtr ours)
    {
        // D3D exclusive fullscreen or presentation mode
        if (SHQueryUserNotificationState(out int state) == 0 && (state == 3 || state == 4))
            return true;

        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == ours) return false;

        var cls = ClassName(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow")
            return false;

        const uint MONITOR_DEFAULTTONEAREST = 2;
        var monFg = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        if (monFg != MonitorFromWindow(ours, MONITOR_DEFAULTTONEAREST)) return false;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monFg, ref mi) || !GetWindowRect(fg, out var r)) return false;

        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top &&
               r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Move a window in one step (x and y together, in physical pixels): setting WPF's Left
    /// and then Top moves it twice per frame, which shows up as a judder while dragging.</summary>
    public static void MoveTo(IntPtr h, int x, int y)
    {
        const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200, SWP_ASYNCWINDOWPOS = 0x4000;
        SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
    }

    /// <summary>Re-assert "always on top" (some apps / the taskbar push topmost windows down).</summary>
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);

    /// <summary>Is h somewhere above "other" in the window order?</summary>
    public static bool IsAbove(IntPtr h, IntPtr other)
    {
        const uint GW_HWNDPREV = 3;
        for (int i = 0; i < 2000 && other != IntPtr.Zero; i++)
            if ((other = GetWindow(other, GW_HWNDPREV)) == h) return true;
        return false;
    }

    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);

    /// <summary>The taskbars: the main one and one per other screen (when Windows shows the taskbar on all screens).</summary>
    public static IEnumerable<IntPtr> Taskbars()
    {
        var main = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
        if (main != IntPtr.Zero) yield return main;
        for (var h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null); h != IntPtr.Zero; h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null))
            yield return h;
    }

    public static void KeepTopmost(IntPtr h)
    {
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
        SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public static void PressKey(byte vk)
    {
        const uint KEYUP = 0x2;
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYUP, UIntPtr.Zero);
    }

    // ------------------------------------------------------------------ Magnification API (MagCapture)

    [StructLayout(LayoutKind.Sequential)]
    public struct MAGIMAGEHEADER
    {
        public uint Width, Height;
        public Guid Format;
        public uint Stride, Offset;
        public UIntPtr Size;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate bool MagScalingCallback(IntPtr hwnd, IntPtr src, MAGIMAGEHEADER srcHeader, IntPtr dst, MAGIMAGEHEADER dstHeader, RECT unclipped, RECT clipped, IntPtr dirty);

    public const int WS_EX_LAYERED = 0x80000, WS_POPUP = unchecked((int)0x80000000), WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000;
    public const int MW_FILTERMODE_EXCLUDE = 0;
    [DllImport("Magnification.dll")] public static extern bool MagInitialize();
    [DllImport("Magnification.dll")] public static extern bool MagSetWindowSource(IntPtr hwnd, RECT rect);
    [DllImport("Magnification.dll")] public static extern bool MagSetWindowFilterList(IntPtr hwnd, int mode, int count, IntPtr[] list);
    [DllImport("Magnification.dll")] public static extern bool MagSetImageScalingCallback(IntPtr hwnd, MagScalingCallback cb);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);

    // ------------------------------------------------------------------ graphics cards

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    public const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1, DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4, DISPLAY_DEVICE_MIRRORING_DRIVER = 0x8;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE dd, uint flags);

    /// <summary>
    /// The graphics cards in the PC now, the one driving the main screen first. Not the registry's list of
    /// display drivers: a card that was taken out keeps its entry there (a Radeon swapped for a GeForce still
    /// showed as the Radeon).
    /// </summary>
    public static List<string> GraphicsCards()
    {
        var cards = new List<(string Name, int Rank)>();
        try
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++, dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() })
            {
                if ((dd.StateFlags & DISPLAY_DEVICE_MIRRORING_DRIVER) != 0 || string.IsNullOrWhiteSpace(dd.DeviceString)) continue;
                int rank = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0 ? 0 : (dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0 ? 1 : 2;
                cards.Add((dd.DeviceString.Trim(), rank));
            }
        }
        catch (Exception e) { Log.Write("graphics cards: " + e.Message); }
        return cards.OrderBy(c => c.Rank).Select(c => c.Name).Distinct().ToList();
    }

    // ------------------------------------------------------------------ where a process runs from

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    /// <summary>The full path of a process's program, or "" (limited rights are enough, also for most elevated ones).</summary>
    public static string ProcessPath(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : "";
        }
        finally { CloseHandle(h); }
    }
}
