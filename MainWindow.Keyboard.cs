using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Caps Lock and the keyboard layout: a short notice on the island when either changes.</summary>
public partial class MainWindow
{
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);

    private bool? _capsWas;
    private int _layoutWas;

    /// <summary>~4 times a second (from OnTick).</summary>
    private void WatchKeyboard()
    {
        if (_demoMedia != null) return;
        bool caps = (GetKeyState(0x14) & 1) != 0;
        if (_capsWas != null && caps != _capsWas && _settings.ShowCapsLock)
            ShowToast("", caps ? L.T("Caps Lock включён") : L.T("Caps Lock выключен"), null, null,
                      caps ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)) : null, ms: 1300);
        _capsWas = caps;

        // the layout belongs to the window you type in
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return;
        uint thread = Native.GetWindowThreadProcessId(fg, out _);
        int lang = (int)((long)GetKeyboardLayout(thread) & 0xFFFF);
        if (lang == 0) return;
        if (_layoutWas != 0 && lang != _layoutWas && _settings.ShowKeyboardLayout)
        {
            string code = "", name = "";
            try
            {
                var c = new CultureInfo(lang);
                code = c.TwoLetterISOLanguageName.ToUpperInvariant();
                name = c.Parent != null && c.Parent.NativeName.Length > 0 ? c.Parent.NativeName : c.NativeName;
                if (name.Length > 0) name = char.ToUpper(name[0]) + name[1..];
            }
            catch { code = $"{lang:X4}"; }
            ShowToast("", code, name, null, null, ms: 1100);
        }
        _layoutWas = lang;
    }
}
