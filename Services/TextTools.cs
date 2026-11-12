using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace DynamicIsland.Services;

/// <summary>Small text transformations for the "Текст" section: all local, nothing leaves the computer.</summary>
public static class TextTools
{
    private const string En = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?@#$^&";
    private const string Ru = "ёйцукенгшщзхъфывапролджэячсмитьбю.ЁЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,\"№;:?";

    /// <summary>"ghbdtn" → "привет" and back: text typed in the wrong keyboard layout.</summary>
    public static string SwapLayout(string s)
    {
        // decide the direction by what the text mostly is
        int latin = s.Count(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        int cyr = s.Count(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
        var (from, to) = latin >= cyr ? (En, Ru) : (Ru, En);
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            int i = from.IndexOf(c);
            sb.Append(i >= 0 ? to[i] : c);
        }
        return sb.ToString();
    }

    public static string Upper(string s) => s.ToUpper(CultureInfo.CurrentCulture);
    public static string Lower(string s) => s.ToLower(CultureInfo.CurrentCulture);

    /// <summary>First letter of every sentence capital, the rest lower.</summary>
    public static string Sentence(string s)
    {
        var chars = s.ToLower(CultureInfo.CurrentCulture).ToCharArray();
        bool start = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                if (start) chars[i] = char.ToUpper(chars[i], CultureInfo.CurrentCulture);
                start = false;
            }
            else if (chars[i] is '.' or '!' or '?' or '\n') start = true;
        }
        return new string(chars);
    }

    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo", ['ж'] = "zh",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu",
        ['я'] = "ya", ['і'] = "i", ['ї'] = "yi", ['є'] = "ye", ['ґ'] = "g",
    };

    public static string Transliterate(string s)
    {
        var sb = new StringBuilder(s.Length * 2);
        foreach (var c in s)
        {
            var lower = char.ToLowerInvariant(c);
            if (!Translit.TryGetValue(lower, out var t)) { sb.Append(c); continue; }
            if (char.IsUpper(c) && t.Length > 0) t = char.ToUpperInvariant(t[0]) + t[1..];
            sb.Append(t);
        }
        return sb.ToString();
    }

    public static string JsonPretty(string s)
    {
        using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        return JsonSerializer.Serialize(doc.RootElement,
            new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public static string Base64Encode(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    public static string Base64Decode(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s.Trim()));
    public static string UrlEncode(string s) => Uri.EscapeDataString(s);
    public static string UrlDecode(string s) => Uri.UnescapeDataString(s.Replace("+", " "));

    public static string Stats(string s)
    {
        int words = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        int lines = s.Length == 0 ? 0 : s.Split('\n').Length;
        return L.F("{0} симв. · {1} слов · {2} строк", s.Length, words, lines);
    }

    /// <summary>QR code picture for any text or link.</summary>
    public static BitmapSource QrCode(string text, int size = 360)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 1, PureBarcode = true },
        };
        writer.Options.Hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
        var data = writer.Write(text);
        var bmp = BitmapSource.Create(data.Width, data.Height, 96, 96, PixelFormats.Bgra32, null, data.Pixels, data.Width * 4);
        bmp.Freeze();
        return bmp;
    }
}
