using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace DynamicIsland.Services;

/// <summary>
/// Translations. The app is written in Russian; every other language is a table
/// "Russian text → translation" (Assets/lang/&lt;code&gt;.json, embedded). Code calls <see cref="T"/> / <see cref="F"/>,
/// and the XAML is translated once after it loads by walking the element tree (<see cref="TranslateTree"/>).
/// Anything missing from a table simply stays in Russian, so a half-done translation never breaks the UI.
/// </summary>
public static class Loc
{
    public static readonly (string Code, string Name)[] Languages =
    [
        ("en", "English"), ("ru", "Русский"), ("uk", "Українська"), ("es", "Español"),
        ("de", "Deutsch"), ("pt", "Português"), ("zh", "中文"),
    ];

    public static string Lang { get; private set; } = "ru";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("ru-RU");
    private static Dictionary<string, string> _table = [];

    /// <summary>"" = follow Windows (falls back to English for languages we don't have).</summary>
    public static string Resolve(string setting)
    {
        if (Languages.Any(l => l.Code == setting)) return setting;
        var os = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (os == "be") os = "ru";
        return Languages.Any(l => l.Code == os) ? os : "en";
    }

    public static void Init(string setting)
    {
        Lang = Resolve(setting);
        Culture = CultureInfo.GetCultureInfo(Lang switch
        {
            "en" => "en-US", "uk" => "uk-UA", "es" => "es-ES", "de" => "de-DE", "pt" => "pt-BR", "zh" => "zh-CN", _ => "ru-RU",
        });
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        Thread.CurrentThread.CurrentCulture = Culture;
        Thread.CurrentThread.CurrentUICulture = Culture;
        try
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(Culture.IetfLanguageTag)));
        }
        catch { /* already set */ }

        _table = [];
        if (Lang == "ru") return;
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"DynamicIsland.Assets.lang.{Lang}.json");
            if (s == null) return;
            _table = JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? [];
        }
        catch (Exception e) { Log.Write("lang: " + e.Message); }
    }

    /// <summary>Translate a Russian UI string.</summary>
    public static string T(string ru) =>
        ru.Length > 0 && _table.TryGetValue(ru, out var t) && t.Length > 0 ? t : ru;

    /// <summary>Translate a Russian format string ("{0} мин") and fill it in.</summary>
    public static string F(string ru, params object?[] args) => string.Format(Culture, T(ru), args);

    private static bool HasCyrillic(string s) => s.Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё');

    /// <summary>Translate every text the XAML declared under <paramref name="root"/>: texts, button captions, tool tips.</summary>
    public static void TranslateTree(DependencyObject root)
    {
        if (Lang == "ru") return;
        var seen = new HashSet<DependencyObject>();
        void Walk(DependencyObject d)
        {
            if (!seen.Add(d)) return;
            switch (d)
            {
                case TextBlock tb when tb.Inlines.Count <= 1 && HasCyrillic(tb.Text):
                    tb.Text = T(tb.Text);
                    break;
                case ContentControl cc when cc.Content is string s && HasCyrillic(s):
                    cc.Content = T(s);
                    break;
                case HeaderedItemsControl hc when hc.Header is string h && HasCyrillic(h):
                    hc.Header = T(h);
                    break;
            }
            if (d is FrameworkElement fe)
            {
                if (fe.ContextMenu != null) Walk(fe.ContextMenu);
                if (fe.ToolTip is string tip && HasCyrillic(tip)) fe.ToolTip = T(tip);
                // "Name|glyph" tags (settings sections) and placeholder tags
                if (fe.Tag is string tag && HasCyrillic(tag))
                {
                    var bar = tag.IndexOf('|');
                    fe.Tag = bar < 0 ? T(tag) : T(tag[..bar]) + tag[bar..];
                }
            }
            if (d is TextBlock { Inlines.Count: > 1 } multi)
                foreach (var run in multi.Inlines.OfType<System.Windows.Documents.Run>())
                    if (HasCyrillic(run.Text)) run.Text = T(run.Text);

            foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
                Walk(child);
        }
        Walk(root);
    }
}
