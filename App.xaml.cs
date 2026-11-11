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
        if (!created)
        {
            Shutdown();
            return;
        }

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); // rasdial output
        var settings = Settings.Current;
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
