using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>
/// "Clean desktop": the Windows taskbar hides itself until you touch the bottom edge, and the desktop
/// icons are hidden, so only the wallpaper, the island and the dock remain. Both are undone when the
/// mode is switched off; nothing is deleted.
/// </summary>
public static class CleanDesktop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    private const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    private const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

    private static IntPtr Taskbar => FindWindow("Shell_TrayWnd", null);

    public static bool TaskbarAutoHide
    {
        get
        {
            var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = Taskbar };
            return ((long)SHAppBarMessage(ABM_GETSTATE, ref d) & ABS_AUTOHIDE) != 0;
        }
        set
        {
            var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = Taskbar, lParam = value ? ABS_AUTOHIDE : ABS_ALWAYSONTOP };
            SHAppBarMessage(ABM_SETSTATE, ref d);
        }
    }

    /// <summary>The list view that draws the desktop icons (it lives under Progman or a WorkerW).</summary>
    private static IntPtr DesktopIcons()
    {
        IntPtr view = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view == IntPtr.Zero)
        {
            EnumWindows((w, _) =>
            {
                var v = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (v != IntPtr.Zero) { view = v; return false; }
                return true;
            }, IntPtr.Zero);
        }
        return view == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
    }

    public static bool IconsVisible
    {
        get { var h = DesktopIcons(); return h == IntPtr.Zero || IsWindowVisible(h); }
        set { var h = DesktopIcons(); if (h != IntPtr.Zero) ShowWindow(h, value ? 5 : 0); }
    }

    /// <summary>
    /// Turn the mode on or off. When switching off, the taskbar and icons go back to how the user had them
    /// (<paramref name="taskbarWasAutoHide"/>, <paramref name="iconsWereVisible"/>), not to some default.
    /// </summary>
    public static void Apply(bool on, bool taskbarWasAutoHide, bool iconsWereVisible)
    {
        try
        {
            // the taskbar stays as the user has it; only the icons go
            IconsVisible = !on && iconsWereVisible;
        }
        catch (Exception e) { Log.Write("clean desktop: " + e.Message); }
    }
}
