using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Services;

/// <summary>
/// Icons the way Explorer draws them, at any size and with transparency — for programs, shortcuts,
/// folders and Microsoft Store apps ("shell:AppsFolder\…"). Call on the UI (STA) thread.
/// </summary>
public static class ShellIcons
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, ref BITMAP bmp);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

    private const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;
    private static readonly Dictionary<(string, int), ImageSource?> Cache = new();

    public static ImageSource? Get(string path, int size)
    {
        if (Cache.TryGetValue((path, size), out var hit)) return hit;
        ImageSource? result = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out var hbm) == 0)
            {
                try { result = FromHBitmap(hbm); }
                finally { DeleteObject(hbm); }
            }
        }
        catch { }
        return Cache[(path, size)] = result;
    }

    /// <summary>Copy the 32-bit DIB with its alpha (the usual WPF helper would drop the transparency).</summary>
    private static BitmapSource? FromHBitmap(IntPtr hbm)
    {
        var bmp = new BITMAP();
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref bmp) == 0 || bmp.bmBits == IntPtr.Zero || bmp.bmBitsPixel != 32) return null;
        int w = bmp.bmWidth, h = Math.Abs(bmp.bmHeight), stride = bmp.bmWidthBytes;
        var pixels = new byte[stride * h];
        Marshal.Copy(bmp.bmBits, pixels, 0, pixels.Length);
        // DIB sections are stored bottom-up: flip the rows
        var flipped = new byte[pixels.Length];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(pixels, y * stride, flipped, (h - 1 - y) * stride, stride);
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, flipped, stride);
        src.Freeze();
        return src;
    }
}
