using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using WinImaging = global::Windows.Graphics.Imaging;
using WinOcr = global::Windows.Media.Ocr;
using ZXing;

namespace DynamicIsland.Services;

/// <summary>Screen capture, OCR (built into Windows), QR decoding and translation.</summary>
public static class CaptureService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static string PicturesFolder
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Dynamic Island");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Whole virtual desktop (all monitors), in physical pixels.</summary>
    public static (BitmapSource Image, Drawing.Rectangle Bounds) CaptureDesktop()
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        return (CaptureRect(bounds), bounds);
    }

    /// <summary>The monitor under the mouse.</summary>
    public static BitmapSource CaptureMonitorUnderCursor()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        return CaptureRect(screen.Bounds);
    }

    private static BitmapSource CaptureRect(Drawing.Rectangle r)
    {
        using var bmp = new Drawing.Bitmap(r.Width, r.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size, Drawing.CopyPixelOperation.SourceCopy);

        var h = bmp.GetHbitmap();
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { DeleteObject(h); }
    }

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

    public static BitmapSource Crop(BitmapSource src, Int32Rect rect)
    {
        var c = new CroppedBitmap(src, rect);
        c.Freeze();
        return c;
    }

    public static string SavePng(BitmapSource bmp, string? path = null)
    {
        path ??= Path.Combine(PicturesFolder, $"Скриншот {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }

    private static byte[] ToBgra(BitmapSource bmp, out int w, out int h)
    {
        var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        w = conv.PixelWidth;
        h = conv.PixelHeight;
        var bytes = new byte[w * h * 4];
        conv.CopyPixels(bytes, w * 4, 0);
        return bytes;
    }

    // ------------------------------------------------------------------ OCR

    public static async Task<string> RecognizeTextAsync(BitmapSource bmp)
    {
        var engine = WinOcr.OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? WinOcr.OcrEngine.TryCreateFromLanguage(new global::Windows.Globalization.Language("en-US"));
        if (engine == null) throw new InvalidOperationException("В Windows не установлен язык для распознавания текста");

        // small crops are recognised much better when upscaled
        BitmapSource input = bmp;
        if (bmp.PixelHeight < 300)
        {
            double k = Math.Min(3, 300.0 / bmp.PixelHeight);
            input = new TransformedBitmap(bmp, new ScaleTransform(k, k));
        }
        if (Math.Max(input.PixelWidth, input.PixelHeight) > WinOcr.OcrEngine.MaxImageDimension)
        {
            double k = (double)WinOcr.OcrEngine.MaxImageDimension / Math.Max(input.PixelWidth, input.PixelHeight);
            input = new TransformedBitmap(input, new ScaleTransform(k, k));
        }

        var bgra = ToBgra(input, out int w, out int h);
        using var sb = new WinImaging.SoftwareBitmap(WinImaging.BitmapPixelFormat.Bgra8, w, h, WinImaging.BitmapAlphaMode.Premultiplied);
        sb.CopyFromBuffer(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bgra));

        var result = await engine.RecognizeAsync(sb);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
    }

    // ------------------------------------------------------------------- QR

    public static string? DecodeQr(BitmapSource bmp)
    {
        var bgra = ToBgra(bmp, out int w, out int h);
        var source = new RGBLuminanceSource(bgra, w, h, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = { TryHarder = true, TryInverted = true },
        };
        return reader.Decode(source)?.Text;
    }

    // ------------------------------------------------------------ translate

    /// <summary>Google Translate public endpoint (no key). Only called when the user presses "Перевести".</summary>
    public static async Task<string> TranslateAsync(string text, string to)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["q"] = text });
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(to)}&dt=t";
        using var resp = await Http.PostAsync(url, content);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var sb = new System.Text.StringBuilder();
        foreach (var part in doc.RootElement[0].EnumerateArray())
            if (part.ValueKind == JsonValueKind.Array && part[0].ValueKind == JsonValueKind.String)
                sb.Append(part[0].GetString());
        return sb.ToString();
    }

    // ----------------------------------------------------------- clean text

    public static string CleanText(string s)
    {
        s = s.Replace(' ', ' ')
             .Replace("​", "").Replace("‌", "").Replace("‍", "").Replace("﻿", "")
             .Replace("\r\n", "\n").Replace('\r', '\n')
             .Replace('“', '"').Replace('”', '"').Replace('„', '"').Replace('«', '"').Replace('»', '"')
             .Replace('‘', '\'').Replace('’', '\'')
             .Replace('\t', ' ');
        var lines = s.Split('\n').Select(l => System.Text.RegularExpressions.Regex.Replace(l, " {2,}", " ").Trim());
        var joined = string.Join(Environment.NewLine, lines);
        return System.Text.RegularExpressions.Regex.Replace(joined, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine).Trim();
    }
}
