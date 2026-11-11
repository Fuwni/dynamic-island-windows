using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland.Services;

public sealed record Place(double Lat, double Lon, string Name);

public sealed record HourForecast(DateTime Time, double Temp, int Code, bool IsDay);

public sealed record WeatherData(
    string Place,
    double Temp,
    double FeelsLike,
    int Code,
    bool IsDay,
    double Humidity,
    double PressureMm,
    double WindSpeed,
    double WindFrom,
    DateTime Sunrise,
    DateTime Sunset,
    double Max,
    double Min,
    double Uv,
    double PrecipNow,
    double PrecipToday,
    double PrecipChance,
    IReadOnlyList<HourForecast> Hours);

/// <summary>Weather from Open-Meteo: free, no API key.</summary>
public sealed class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<Place?> ResolveAsync(string city)
    {
        if (string.IsNullOrWhiteSpace(city)) return null;
        return await GeocodeAsync(city.Trim());
    }

    private static async Task<Place?> GeocodeAsync(string city)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=ru&format=json";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;
        var r = results[0];
        return new Place(
            r.GetProperty("latitude").GetDouble(),
            r.GetProperty("longitude").GetDouble(),
            r.TryGetProperty("name", out var n) ? n.GetString() ?? city : city);
    }

    public async Task<WeatherData> GetAsync(Place place)
    {
        var url =
            "https://api.open-meteo.com/v1/forecast?latitude=" + place.Lat.ToString(Inv) +
            "&longitude=" + place.Lon.ToString(Inv) +
            "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,surface_pressure,wind_speed_10m,wind_direction_10m,uv_index,precipitation" +
            "&hourly=temperature_2m,weather_code,is_day" +
            "&daily=sunrise,sunset,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max" +
            "&timezone=auto&forecast_days=2&wind_speed_unit=ms";

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var root = doc.RootElement;
        var cur = root.GetProperty("current");
        var daily = root.GetProperty("daily");
        var hourly = root.GetProperty("hourly");

        var now = DateTime.Parse(cur.GetProperty("time").GetString()!, Inv);
        var times = hourly.GetProperty("time");
        var temps = hourly.GetProperty("temperature_2m");
        var codes = hourly.GetProperty("weather_code");
        var days = hourly.GetProperty("is_day");

        var hours = new List<HourForecast>();
        for (int i = 0; i < times.GetArrayLength() && hours.Count < 6; i++)
        {
            var t = DateTime.Parse(times[i].GetString()!, Inv);
            if (t <= now) continue;
            hours.Add(new HourForecast(t, temps[i].GetDouble(), codes[i].GetInt32(), days[i].GetInt32() == 1));
        }

        return new WeatherData(
            place.Name,
            cur.GetProperty("temperature_2m").GetDouble(),
            cur.GetProperty("apparent_temperature").GetDouble(),
            cur.GetProperty("weather_code").GetInt32(),
            cur.GetProperty("is_day").GetInt32() == 1,
            cur.GetProperty("relative_humidity_2m").GetDouble(),
            cur.GetProperty("surface_pressure").GetDouble() * 0.750062,
            cur.GetProperty("wind_speed_10m").GetDouble(),
            cur.GetProperty("wind_direction_10m").GetDouble(),
            DateTime.Parse(daily.GetProperty("sunrise")[0].GetString()!, Inv),
            DateTime.Parse(daily.GetProperty("sunset")[0].GetString()!, Inv),
            daily.GetProperty("temperature_2m_max")[0].GetDouble(),
            daily.GetProperty("temperature_2m_min")[0].GetDouble(),
            Num(cur, "uv_index"),
            Num(cur, "precipitation"),
            Num(daily, "precipitation_sum", 0),
            Num(daily, "precipitation_probability_max", 0),
            hours);
    }

    private static double Num(JsonElement obj, string name, int? index = null)
    {
        if (!obj.TryGetProperty(name, out var v)) return 0;
        if (index is int i) v = v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > i ? v[i] : default;
        return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    }

    // WMO weather codes -> text / glyph (Segoe UI Symbol).
    public static string Describe(int code) => code switch
    {
        0 => "Ясно",
        1 => "Малооблачно",
        2 => "Переменная облачность",
        3 => "Пасмурно",
        45 or 48 => "Туман",
        >= 51 and <= 57 => "Морось",
        >= 61 and <= 67 => "Дождь",
        >= 71 and <= 77 => "Снег",
        >= 80 and <= 82 => "Ливень",
        85 or 86 => "Снегопад",
        >= 95 => "Гроза",
        _ => "—",
    };

    public static string Glyph(int code, bool isDay) => code switch
    {
        0 or 1 => isDay ? "☀" : "☾",
        2 => isDay ? "⛅" : "☁",
        3 => "☁",
        45 or 48 => "≋",
        >= 51 and <= 67 => "☂",
        >= 80 and <= 82 => "☂",
        >= 71 and <= 77 => "❄",
        85 or 86 => "❄",
        >= 95 => "⛈",
        _ => "☁",
    };

    public static string Compass(double degrees)
    {
        string[] names = ["С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ"];
        return names[(int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8];
    }
}
