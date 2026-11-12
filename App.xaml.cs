using System.Windows;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "DynamicIsland.SingleInstance", out bool created);
        if (!created && e.Args.Contains("--restart"))
        {
            // relaunched after a language change: wait for the old instance to let go
            try { created = _mutex.WaitOne(TimeSpan.FromSeconds(8)); }
            catch (AbandonedMutexException) { created = true; }
        }
        if (!created)
        {
            Shutdown();
            return;
        }

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); // rasdial output
        var settings = Settings.Current;
        // "--lang de" previews another language without touching the saved setting
        int langArg = Array.IndexOf(e.Args, "--lang");
        Loc.Init(langArg >= 0 && langArg + 1 < e.Args.Length ? e.Args[langArg + 1] : settings.Language);
        Theme.Apply(settings.DarkTheme, settings.IslandColor, settings.GlassOpacity);

        var window = new MainWindow(e.Args);
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
