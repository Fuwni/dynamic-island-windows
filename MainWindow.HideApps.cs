using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Programs in front of which the island hides (Settings > Behavior), like over a full-screen game.</summary>
public partial class MainWindow
{
    private uint _hiddenPid;
    private string _hiddenPidName = "";

    private bool IsHiddenApp(IntPtr fg)
    {
        if (_settings.HideInApps.Count == 0 || fg == IntPtr.Zero || fg == _hwnd) return false;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return false;
        if (pid != _hiddenPid)
        {
            _hiddenPid = pid;
            try { using var p = Process.GetProcessById((int)pid); _hiddenPidName = p.ProcessName; }
            catch { _hiddenPidName = ""; }
        }
        return _settings.HideInApps.Any(a => a.Equals(_hiddenPidName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Running programs with a window, for the picker.</summary>
    private void FillHideAppPicker()
    {
        HideAppPicker.Items.Clear();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                if (p.ProcessName is "explorer" or "ApplicationFrameHost" or "TextInputHost") continue;
                names.Add(p.ProcessName);
            }
            catch { }
            finally { p.Dispose(); }
        }
        foreach (var n in names) HideAppPicker.Items.Add(n);
        BuildHideAppChips();
    }

    private void BuildHideAppChips()
    {
        HideAppChips.Children.Clear();
        foreach (var name in _settings.HideInApps)
        {
            var b = new Button { Style = (Style)FindResource("GhostPill"), Padding = new Thickness(12, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6), ToolTip = L.T("Убрать") };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = "  \u2715", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
            b.Content = row;
            var n = name;
            b.Click += (_, _) => { _settings.HideInApps.Remove(n); _settings.Save(); BuildHideAppChips(); Remeasure(); };
            HideAppChips.Children.Add(b);
        }
        HideAppsEmpty.Visibility = _settings.HideInApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideAppAdd_Click(object sender, RoutedEventArgs e)
    {
        var name = (HideAppPicker.SelectedItem as string ?? HideAppPicker.Text ?? "").Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0 || _settings.HideInApps.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        _settings.HideInApps.Add(name);
        _settings.Save();
        BuildHideAppChips();
        Remeasure();
    }
}
