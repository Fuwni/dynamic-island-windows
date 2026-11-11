using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Services;

/// <summary>
/// Swaps the app-wide brushes: dark / light theme, island colour and the "liquid glass" look
/// (see-through tint, light rim along the edge and a soft specular shine at the top).
/// </summary>
public static class Theme
{
    public static readonly string[] Palette =
    [
        "#000000", "#1C1C1E", "#1B2A4A", "#16324F", "#123B32",
        "#3A1F4D", "#4A1C2C", "#4A2E12", "#2E2E2E", "#0F3D3E",
    ];

    public static void Apply(bool dark, string islandHex, double glass = 1)
    {
        var island = Parse(islandHex) ?? Colors.Black;
        byte a = (byte)Math.Round(Math.Clamp(glass, 0.4, 1) * 255);
        var r = Application.Current.Resources;

        if (dark)
        {
            r["IslandBg"] = Solid(Color.FromArgb(a, island.R, island.G, island.B));
            r["GlassRim"] = Gradient(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF), 0.0, 0.7);
            r["GlassShine"] = Solid(Colors.Transparent);
            r["TileBg"] = Gradient(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
            r["TileBgHover"] = Gradient(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            r["TileStroke"] = Gradient(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF));
            r["Fg"] = Solid(Colors.White);
            r["Fg2"] = Solid(Color.FromRgb(0xB4, 0xB4, 0xBA));
            r["Fg3"] = Solid(Color.FromRgb(0x7C, 0x7C, 0x84));
            r["TrackBg"] = Solid(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
            r["ChipBg"] = Solid(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            r["PlayBg"] = Solid(Colors.White);
            r["PlayFg"] = Solid(Colors.Black);
        }
        else
        {
            var bg = island == Colors.Black ? Color.FromRgb(0xF6, 0xF6, 0xF9) : Mix(island, Colors.White, 0.86);
            r["IslandBg"] = Solid(Color.FromArgb(a, bg.R, bg.G, bg.B));
            r["GlassRim"] = Solid(Color.FromArgb(0x16, 0x00, 0x00, 0x00));
            r["GlassShine"] = Solid(Colors.Transparent);
            r["TileBg"] = Gradient(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF));
            r["TileBgHover"] = Solid(Colors.White);
            r["TileStroke"] = Gradient(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0x00, 0x00, 0x00));
            r["Fg"] = Solid(Color.FromRgb(0x1C, 0x1C, 0x1E));
            r["Fg2"] = Solid(Color.FromRgb(0x5E, 0x5E, 0x64));
            r["Fg3"] = Solid(Color.FromRgb(0x98, 0x98, 0x9E));
            r["TrackBg"] = Solid(Color.FromArgb(0x1E, 0x00, 0x00, 0x00));
            r["ChipBg"] = Solid(Color.FromArgb(0x12, 0x00, 0x00, 0x00));
            r["PlayBg"] = Solid(Color.FromRgb(0x1C, 0x1C, 0x1E));
            r["PlayFg"] = Solid(Colors.White);
        }
    }

    public static Color? Parse(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return null; }
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static SolidColorBrush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush Gradient(Color top, Color bottom, double from = 0, double to = 1)
    {
        var b = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, from),
            EndPoint = new Point(0.5, to),
            GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
        };
        b.Freeze();
        return b;
    }
}
