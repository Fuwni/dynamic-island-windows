using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Themes: ready-made looks (island colour, accent, light / dark, glow), more from the online theme
/// gallery (a plain list of colours on GitHub, no code), and your own photo as the island's background.
/// </summary>
public partial class MainWindow
{
    public sealed record ThemePreset(string Id, string Name, bool Dark, string Island, string Accent, string Aura);

    private static readonly ThemePreset[] BuiltInThemes =
    [
        new("midnight", "Полночь", true, "#000000", "#30D158", "none"),
        new("graphite", "Графит", true, "#1C1C1E", "#0A84FF", "mono"),
        new("ocean", "Океан", true, "#0F2A44", "#4CB8FF", "ocean"),
        new("forest", "Лес", true, "#10291F", "#34D399", "none"),
        new("sunset", "Закат", true, "#2B1408", "#FF9F0A", "sunset"),
        new("rose", "Роза", true, "#2E0F1C", "#FF5C8A", "sunset"),
        new("lavender", "Лаванда", true, "#1F1433", "#BF5AF2", "aurora"),
        new("neon", "Неон", true, "#07070F", "#00F0FF", "neon"),
        new("snow", "Снег", false, "#000000", "#0A84FF", "none"),
        new("sand", "Песок", false, "#8A6A3B", "#D97706", "none"),
        new("mint", "Мята", false, "#2F7D62", "#10B981", "none"),
        new("sky", "Небо", false, "#2B5C8A", "#3B82F6", "ocean"),
    ];

    private const string ThemeStoreUrl = "https://raw.githubusercontent.com/Fuwni/dynamic-island-windows/main/themes/themes.json";
    private static string PhotoPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "island-photo.jpg");

    // ---------------------------------------------------------------- gallery

    private void BuildThemeGallery()
    {
        ThemeGallery.Children.Clear();
        foreach (var t in BuiltInThemes) ThemeGallery.Children.Add(ThemeCard(t, L.T(t.Name)));
        PhotoRemoveButton.Visibility = _settings.IslandPhoto ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement ThemeCard(ThemePreset t, string name)
    {
        var island = Theme.Parse(t.Island) ?? Colors.Black;
        var accent = Theme.Parse(t.Accent) ?? Colors.LimeGreen;
        var bg = t.Dark ? island : Blend(island, Colors.White, island == Colors.Black ? 0.95 : 0.8);
        var pill = new Border
        {
            Width = 46, Height = 14, CornerRadius = new CornerRadius(7), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(t.Dark ? Color.FromRgb(0, 0, 0) : Blend(island, Colors.White, 0.86)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(t.Dark ? 0.6 : 0),
            Child = new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(7, 0, 0, 0) },
        };
        bool current = _settings.ThemeId == t.Id;
        var card = new Border
        {
            Width = 96, Height = 58, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(bg), Child = pill,
            BorderThickness = new Thickness(2), BorderBrush = current ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
            Cursor = System.Windows.Input.Cursors.Hand, ToolTip = name,
        };
        var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 10) };
        stack.Children.Add(card);
        var label = new TextBlock { Text = name, FontSize = 11.5, Margin = new Thickness(2, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 96 };
        stack.Children.Add(label);
        card.MouseLeftButtonUp += (_, _) => ApplyThemePreset(t);
        return stack;
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private void ApplyThemePreset(ThemePreset t)
    {
        _settings.ThemeId = t.Id;
        _settings.DarkTheme = t.Dark;
        _settings.IslandColor = t.Island;
        _settings.AccentColor = t.Accent;
        _settings.Aura = t.Aura;
        _settings.Save();
        ApplyTheme();
        ApplyThemeLocal();
        ApplyGlass();
        SetupAura();
        DarkSwitch.IsChecked = _settings.DarkTheme;
        ColorBox.Text = _settings.IslandColor;
        BuildThemeGallery();
        if (ThemeStore.Children.Count > 0) _ = LoadThemeStoreAsync();
        Kick(4);
        PulseGlow(Theme.Parse(t.Accent) ?? Colors.White);
    }

    // ---------------------------------------------------------------- online gallery

    private static readonly HttpClient ThemeHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    private async void ThemeStore_Click(object sender, RoutedEventArgs e) => await LoadThemeStoreAsync();

    /// <summary>
    /// The theme gallery is a list of colours kept in the project's GitHub repository. Only names and
    /// colours are read (nothing runs), every value is checked, at most 60 themes.
    /// </summary>
    private async Task LoadThemeStoreAsync()
    {
        ThemeStoreStatus.Text = L.T("Загружаю…");
        try
        {
            using var doc = JsonDocument.Parse(await ThemeHttp.GetStringAsync(ThemeStoreUrl));
            ThemeStore.Children.Clear();
            int n = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (n >= 60) break;
                string Str(string k) => el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var id = Str("id"); var name = Str("name"); var island = Str("island"); var accent = Str("accent"); var aura = Str("aura");
                bool dark = !el.TryGetProperty("dark", out var dv) || dv.ValueKind != JsonValueKind.False;
                if (id.Length is 0 or > 40 || name.Length is 0 or > 30) continue;
                if (!IsHex(island) || !IsHex(accent)) continue;
                if (Auras.All(a => a.Id != aura)) aura = "none";
                var author = Str("author");
                var t = new ThemePreset("store:" + id, name, dark, island, accent, aura);
                var card = ThemeCard(t, author.Length is > 0 and <= 30 ? $"{name} · {author}" : name);
                ThemeStore.Children.Add(card);
                n++;
            }
            ThemeStoreStatus.Text = n > 0 ? L.F("Тем в галерее: {0}. Хотите свою? Пришлите её в Telegram-канал.", n) : L.T("Галерея пока пуста");
        }
        catch
        {
            ThemeStoreStatus.Text = L.T("Не удалось загрузить галерею — проверьте интернет");
        }
        Remeasure();
    }

    private static bool IsHex(string s) =>
        System.Text.RegularExpressions.Regex.IsMatch(s, "^#[0-9A-Fa-f]{6}$");

    // ---------------------------------------------------------------- your own photo

    private void Photo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.T("Фото для острова"),
            Filter = L.T("Изображения") + "|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            // keep our own small copy: the original can move or be deleted
            var src = new BitmapImage();
            src.BeginInit();
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.UriSource = new Uri(dlg.FileName);
            src.DecodePixelWidth = 1400;
            src.EndInit();
            src.Freeze();
            Directory.CreateDirectory(Path.GetDirectoryName(PhotoPath)!);
            var enc = new JpegBitmapEncoder { QualityLevel = 90 };
            enc.Frames.Add(BitmapFrame.Create(src));
            using (var fs = File.Create(PhotoPath)) enc.Save(fs);
            _settings.IslandPhoto = true;
            _settings.Save();
            ApplyPhoto();
            BuildThemeGallery();
            Kick(4);
        }
        catch (Exception ex)
        {
            ShowToast("", L.T("Не получилось открыть фото"), ex.Message);
        }
    }

    private void PhotoRemove_Click(object sender, RoutedEventArgs e)
    {
        _settings.IslandPhoto = false;
        _settings.Save();
        try { File.Delete(PhotoPath); } catch { }
        ApplyPhoto();
        BuildThemeGallery();
    }

    /// <summary>Shows (or hides) the photo inside the island, under a tint that keeps the text readable.</summary>
    private void ApplyPhoto()
    {
        BitmapImage? img = null;
        if (_settings.IslandPhoto && File.Exists(PhotoPath))
        {
            try
            {
                img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(PhotoPath);
                img.EndInit();
                img.Freeze();
            }
            catch { img = null; }
        }
        PhotoImage.Source = img;
        PhotoHost.Visibility = img != null ? Visibility.Visible : Visibility.Collapsed;
        ApplyGlass();
    }

    private bool HasPhoto => PhotoHost.Visibility == Visibility.Visible;
}
