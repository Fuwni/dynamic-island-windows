using System.IO;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// "--live-demo &lt;command-file&gt;": demo data only (nothing of the user's is shown or saved), but the
/// island reacts to the REAL mouse, so a screen recording looks like ordinary use. Things that
/// normally come from outside (a Discord call, a message, a finished download, Claude Code asking)
/// are triggered by lines appended to the command file:
///   idle on|off · music · timer &lt;min&gt; · timer off · download · discord on|off · message &lt;who&gt;|&lt;text&gt;
///   cmd &lt;text&gt; · cmd off · char &lt;id&gt; · emote &lt;name&gt; · typing on|off · claude-ask · claude-done
///   expand &lt;tab&gt; · compact · aura &lt;id&gt; · sketch on|off · dock on|off · hover on|off · quit
/// </summary>
public partial class MainWindow
{
    private bool _liveDemo;
    private bool _demoDiscord;

    private void StartLiveDemo(string file)
    {
        _liveDemo = true;
        _fakeCursor = null;               // the real cursor drives the island
        _demoIdle = true;
        _settings.Aura = "none";
        file = Path.GetFullPath(file);
        File.WriteAllText(file, "");
        long read = 0;
        var poll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        poll.Tick += (_, _) =>
        {
            string chunk;
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length <= read) return;
                fs.Position = read;
                using var sr = new StreamReader(fs, System.Text.Encoding.UTF8);
                chunk = sr.ReadToEnd();
                if (!chunk.EndsWith('\n')) return;   // wait for the whole line
                read = fs.Length;
            }
            catch (IOException) { return; }
            foreach (var line in chunk.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try { _ = DemoCommand(line); }
                catch (Exception e) { Log.Write("live-demo: " + line + ": " + e.Message); }
            }
        };
        poll.Start();
    }

    private async Task DemoCommand(string line)
    {
        var sp = line.IndexOf(' ');
        var verb = sp < 0 ? line : line[..sp];
        var arg = sp < 0 ? "" : line[(sp + 1)..];
        Log.Write("live-demo: " + line);
        switch (verb)
        {
            case "idle":
                _demoIdle = arg != "off";
                UpdateMediaUi();
                break;
            case "music":   // start the demo song again just before the chorus, so the lyrics roll on cue
                _demoMedia = _demoMedia! with { Position = TimeSpan.FromSeconds(58), PositionUpdatedAt = DateTimeOffset.Now, IsPlaying = true };
                _demoIdle = false;
                UpdateMediaUi();
                break;
            case "timer":
                if (arg == "off") { _timer.Reset(); break; }
                _timer.SetKind(TimerKind.Timer);
                _timer.SetDuration(TimeSpan.FromMinutes(double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)));
                _timer.Start();
                break;
            case "download":
                ShowToast("", "Загрузка завершена", "presentation.pdf", null, (Brush)FindResource("AccentBlue"), ms: 3200);
                break;
            case "discord":
                _demoDiscord = arg == "on";
                _discordInCall = _demoDiscord;
                _discordSpeaking = _demoDiscord;
                break;
            case "message":
                var parts = arg.Split('|', 2);
                _unread++;
                ShowToast("", parts[0], parts.Length > 1 ? parts[1] : null, null,
                    new SolidColorBrush(Color.FromRgb(0x2A, 0xAB, 0xEE)), ms: 3600);
                break;
            case "cmd":
                if (arg == "off") { CmdBarBox.Text = ""; SetMode(Mode.Compact); break; }
                CmdBarBox.Text = "";
                OpenCommandBar();
                await Task.Delay(450);
                foreach (var ch in arg)
                {
                    CmdBarBox.Text += ch;
                    CmdBarBox.CaretIndex = CmdBarBox.Text.Length;
                    await Task.Delay(95 + Random.Shared.Next(70));
                }
                break;
            case "char":
                UseCharacter(arg);
                Character.Cheer();
                break;
            case "emote":
                Character.Emote(Enum.Parse<Views.Emotion>(arg, true));
                break;
            case "typing":
                _menuTypedAt = arg == "on" ? DateTime.Now.AddSeconds(60) : default;
                break;
            case "claude-ask":
                EnqueuePermission(new ClaudeBridge.PermissionRequest { Tool = "Bash", Details = "dotnet build -c Release", Project = "dynamic-island" });
                break;
            case "claude-done":
                OnClaudeNotice(new ClaudeBridge.Notice("stop", "", "Сборка прошла успешно", "dynamic-island"));
                break;
            case "expand":
                _pinnedOpen = true;
                var tab = arg switch
                {
                    "music" => TabMusic, "weather" => TabWeather, "chat" => TabChat, "timer" => TabTimer,
                    "clip" => TabClip, "notes" => TabNotes, "calendar" => TabCalendar, "system" => TabSystem,
                    "settings" => TabSettings, _ => TabHome,
                };
                tab.IsChecked = true;
                Expand();
                break;
            case "compact":
                _pinnedOpen = false;
                SetMode(Mode.Compact);
                break;
            case "aura":
                _settings.Aura = arg;
                SetupAura();
                break;
            case "sketch":
                _settings.HandDrawn = arg == "on";
                break;
            case "dock":
                _settings.Docked = arg == "on";
                UpdateShape(false);
                break;
            case "hover":   // off: a click opens it, and a click on the character makes it emote
                _settings.ExpandOnHover = arg != "off";
                break;
            case "quit":
                Application.Current.Shutdown();
                break;
        }
    }
}
