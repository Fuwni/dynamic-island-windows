using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Weather tab: tiles like in the iOS weather app.</summary>
public partial class MainWindow
{
    private readonly WeatherService _weather = new();
    private readonly DispatcherTimer _weatherRefresh = new() { Interval = TimeSpan.FromMinutes(15) };

    private async Task InitWeatherAsync()
    {
        CityBox.Text = _settings.City;
        DrawPressureArc(PressureArcBg, 1);
        _weatherRefresh.Tick += async (_, _) => await RefreshWeatherAsync();
        _weatherRefresh.Start();
        await RefreshWeatherAsync();
    }

    private bool _weatherLoaded;
    private int _weatherFailures;

    private async Task RefreshWeatherAsync()
    {
        WeatherStatus.Text = "Обновляю…";
        try
        {
            var place = await _weather.ResolveAsync(_settings.City);
            if (place == null)
            {
                WeatherStatus.Text = string.IsNullOrWhiteSpace(_settings.City) ? "Впишите город и нажмите Enter" : "Город не найден";
                HomeWDesc.Text = "Укажите город";
                _weatherRefresh.Interval = TimeSpan.FromMinutes(15);
                return;
            }
            ApplyWeather(await _weather.GetAsync(place));
            WeatherStatus.Text = "Обновлено в " + DateTime.Now.ToString("HH:mm");
            _weatherLoaded = true;
            _weatherFailures = 0;
            _weatherRefresh.Interval = TimeSpan.FromMinutes(15);
        }
        catch (Exception ex)
        {
            // Typical right after Windows starts: the island is up before the network is.
            // The city is still saved - just try again soon (5 s, 10 s, 20 s … up to a minute).
            _weatherFailures++;
            if (_weatherFailures == 1 || _weatherFailures % 20 == 0) Log.Write("weather: " + ex.Message);
            var retry = TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Min(4, _weatherFailures - 1))));
            _weatherRefresh.Interval = retry;
            WeatherStatus.Text = $"Нет связи, повторю через {(int)retry.TotalSeconds} с";
            if (!_weatherLoaded) HomeWDesc.Text = $"{_settings.City} · жду сеть…";
        }
    }

    private void ApplyWeather(WeatherData w)
    {
        string glyph = WeatherService.Glyph(w.Code, w.IsDay);
        string desc = WeatherService.Describe(w.Code);
        Brush iconBrush = glyph is "☀" or "⛅"
            ? (Brush)FindResource("Warm")
            : new SolidColorBrush(Color.FromRgb(0xA8, 0xC4, 0xE0));

        CompactWeatherIcon.Text = HomeWIcon.Text = NowIcon.Text = glyph;
        CompactWeatherIcon.Foreground = HomeWIcon.Foreground = NowIcon.Foreground = iconBrush;
        CompactTemp.Text = HomeWTemp.Text = NowTemp.Text = Deg(w.Temp);
        HomeWDesc.Text = $"{desc} · {w.Place}";

        NowPlace.Text = w.Place.ToUpperInvariant();
        NowDesc.Text = desc;
        NowFeels.Text = $"Ощущается {Deg(w.FeelsLike)} · ↑{Deg(w.Max)} ↓{Deg(w.Min)}";

        WindSpeed.Text = $"{w.WindSpeed:0.#} м/с";
        WindDir.Text = WeatherService.Compass(w.WindFrom);
        WindArrow.Angle = w.WindFrom + 180; // where the wind blows to

        HumidityText.Text = $"{Math.Round(w.Humidity)}%";
        HumidityScale.ScaleX = Math.Clamp(w.Humidity / 100, 0, 1);
        DewText.Text = w.Humidity < 35 ? "Сухо" : w.Humidity < 65 ? "Комфортно" : w.Humidity < 85 ? "Влажно" : "Очень влажно";

        PressureText.Text = Math.Round(w.PressureMm).ToString();
        DrawPressureArc(PressureArc, Math.Clamp((w.PressureMm - 720) / 70, 0.02, 1));

        // sun on its arc
        double t = (DateTime.Now - w.Sunrise).TotalMinutes / Math.Max(1, (w.Sunset - w.Sunrise).TotalMinutes);
        bool day = t is >= 0 and <= 1;
        t = Math.Clamp(t, 0, 1);
        double x = (1 - t) * (1 - t) * 4 + 2 * (1 - t) * t * 60 + t * t * 116;
        double y = (1 - t) * (1 - t) * 40 + 2 * (1 - t) * t * -18 + t * t * 40;
        Canvas.SetLeft(SunDot, x - 6);
        Canvas.SetTop(SunDot, y - 6);
        SunDot.Opacity = day ? 1 : 0.35;
        SunriseText.Text = "↑ " + w.Sunrise.ToString("HH:mm");
        SunsetText.Text = "↓ " + w.Sunset.ToString("HH:mm");

        UvText.Text = Math.Round(w.Uv).ToString();
        UvDesc.Text = w.Uv < 3 ? "Низкий" : w.Uv < 6 ? "Умеренный" : w.Uv < 8 ? "Высокий" : w.Uv < 11 ? "Очень высокий" : "Экстремальный";
        Canvas.SetLeft(UvMarker, Math.Clamp(w.Uv / 11, 0, 1) * 122 - 5);

        PrecipText.Text = $"{w.PrecipToday:0.#} мм";
        PrecipDesc.Text = w.PrecipNow > 0
            ? $"Сейчас идёт: {w.PrecipNow:0.#} мм · вероятность {Math.Round(w.PrecipChance)}%"
            : $"За сегодня · вероятность {Math.Round(w.PrecipChance)}%";

        HoursPanel.Children.Clear();
        foreach (var h in w.Hours.Take(4))
        {
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(new TextBlock { Text = h.Time.ToString("HH"), FontSize = 10.5, Foreground = (Brush)FindResource("Fg3"), HorizontalAlignment = HorizontalAlignment.Center });
            col.Children.Add(new TextBlock { Text = WeatherService.Glyph(h.Code, h.IsDay), FontFamily = (FontFamily)FindResource("SymbolFont"), FontSize = 15, Margin = new Thickness(0, 5, 0, 5), HorizontalAlignment = HorizontalAlignment.Center });
            col.Children.Add(new TextBlock { Text = Deg(h.Temp), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center });
            HoursPanel.Children.Add(col);
        }
    }

    /// <summary>240° gauge arc, filled to <paramref name="fraction"/>.</summary>
    private static void DrawPressureArc(System.Windows.Shapes.Path path, double fraction)
    {
        const double cx = 39, cy = 38, r = 32, start = 210, sweep = 240;
        Point P(double deg) => new(cx + r * Math.Cos(deg * Math.PI / 180), cy - r * Math.Sin(deg * Math.PI / 180));
        double end = start - sweep * fraction;
        var fig = new PathFigure { StartPoint = P(start), IsClosed = false };
        fig.Segments.Add(new ArcSegment(P(end), new Size(r, r), 0, sweep * fraction > 180, SweepDirection.Clockwise, true));
        path.Data = new PathGeometry([fig]);
    }

    private static string Deg(double t) => $"{(int)Math.Round(t)}°";

    private async void CityBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _settings.City = CityBox.Text.Trim();
        _settings.Save();
        Keyboard.ClearFocus();
        await RefreshWeatherAsync();
    }

    private async void RefreshWeather_Click(object sender, RoutedEventArgs e)
    {
        if (CityBox.Text.Trim() != _settings.City)
        {
            _settings.City = CityBox.Text.Trim();
            _settings.Save();
        }
        await RefreshWeatherAsync();
    }

    private void HomeWeather_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_justDragged) TabWeather.IsChecked = true;
    }
}
