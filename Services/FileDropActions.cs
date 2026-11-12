using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using global::Windows.ApplicationModel.DataTransfer;
using global::Windows.Storage;

namespace DynamicIsland.Services;

/// <summary>What you can do with files dropped on the island.</summary>
public static class FileDropActions
{
    private static readonly string[] ImageExt = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".ico"];

    public static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Puts the files on the clipboard - paste them into Telegram, a chat, a folder…</summary>
    public static void CopyFiles(IEnumerable<string> files)
    {
        var list = new StringCollection();
        list.AddRange(files.ToArray());
        System.Windows.Clipboard.SetFileDropList(list);
    }

    public static string Zip(IReadOnlyList<string> paths)
    {
        var dir = Path.GetDirectoryName(paths[0])!;
        var name = paths.Count == 1 ? Path.GetFileNameWithoutExtension(paths[0]) : L.T("Архив") + DateTime.Now.ToString(" yyyy-MM-dd HH-mm");
        var zip = Unique(Path.Combine(dir, name + ".zip"));
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                var root = Path.GetDirectoryName(p.TrimEnd('\\'))!;
                foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                    archive.CreateEntryFromFile(f, Path.GetRelativePath(root, f), CompressionLevel.Optimal);
            }
            else archive.CreateEntryFromFile(p, Path.GetFileName(p), CompressionLevel.Optimal);
        }
        return zip;
    }

    /// <summary>Converts / shrinks an image. maxSide 0 = keep size.</summary>
    public static string ConvertImage(string path, string format, int quality = 90, int maxSide = 0)
    {
        BitmapSource src;
        using (var fs = File.OpenRead(path))
        {
            var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            src = dec.Frames[0];
        }
        if (maxSide > 0 && Math.Max(src.PixelWidth, src.PixelHeight) > maxSide)
        {
            double k = (double)maxSide / Math.Max(src.PixelWidth, src.PixelHeight);
            src = new TransformedBitmap(src, new ScaleTransform(k, k));
        }

        BitmapEncoder enc = format == "png"
            ? new PngBitmapEncoder()
            : new JpegBitmapEncoder { QualityLevel = quality };
        if (enc is JpegBitmapEncoder)
            src = new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0); // JPEG has no alpha
        enc.Frames.Add(BitmapFrame.Create(src));

        var suffix = maxSide > 0 ? L.T(" (сжато)") : "";
        var output = Unique(Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + suffix + "." + format));
        using (var outFs = File.Create(output)) enc.Save(outFs);
        return output;
    }

    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    // ------------------------------------------------------------------ Windows share sheet

    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid DtmIid = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    /// <summary>Opens the Windows "Share" panel (nearby sharing, mail, apps…).</summary>
    public static void Share(IntPtr hwnd, IReadOnlyList<string> files)
    {
        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var iid = DtmIid;
        var ptr = interop.GetForWindow(hwnd, ref iid);
        var dtm = WinRT.MarshalInterface<DataTransferManager>.FromAbi(ptr);
        dtm.DataRequested += async (_, args) =>
        {
            var deferral = args.Request.GetDeferral();
            try
            {
                var items = new List<IStorageItem>();
                foreach (var f in files)
                    items.Add(Directory.Exists(f) ? await StorageFolder.GetFolderFromPathAsync(f) : await StorageFile.GetFileFromPathAsync(f));
                args.Request.Data.Properties.Title = files.Count == 1 ? Path.GetFileName(files[0]) : L.F("{0} файла(ов)", files.Count);
                args.Request.Data.SetStorageItems(items);
            }
            finally { deferral.Complete(); }
        };
        interop.ShowShareUIForWindow(hwnd);
    }
}
