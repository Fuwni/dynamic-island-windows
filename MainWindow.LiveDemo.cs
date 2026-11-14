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
///   expand &lt;tab&gt; · compact · aura &lt;id&gt; · sketch on|off · dock on|off · hover on|off
///   clock HH:mm|off · sleep on|off · game on|off · remind <text> · pomodoro · appsearch <text>|off · fps <n>|off · quit
/// </summary>
public partial class MainWindow
{
    private bool _liveDemo;
    private bool _demoDiscord;
    private string? _demoClock;   // the time the island shows (a day squeezed into a timelapse)
    private bool _demoSleep, _demoGame;

    private bool _demoHide;

    private void StartLiveDemo(string file)
    {
        _liveDemo = true;
        _fakeCursor = null;               // the real cursor drives the island
        _demoIdle = true;
        _settings.Aura = "none";
        _settings.DockApps =
        [
            @"C:\Windows\explorer.exe",
            @"shell:AppsFolder\MSEdge",
            @"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
            @"shell:AppsFolder\TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm!Telegram.TelegramDesktop.Store",
            @"shell:AppsFolder\com.squirrel.Discord.Discord",
        ];
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
                _settings.Karaoke = true;   // the lyrics roll right in the pill
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
            case "clock":   // clock 08:00 · clock off
                _demoClock = arg == "off" ? null : arg;
                OnTick();
                break;
            case "sleep":
                _demoSleep = arg == "on";
                break;
            case "game":    // pretend a fullscreen game runs: the pill shrinks to CPU / GPU / RAM
                _demoGame = arg == "on";
                break;
            case "remind":
                ShowToast("", L.T("Напоминание"), arg, null, (Brush)FindResource("Warm"), ms: 4200);
                break;
            case "pomodoro":
                _timer.SetKind(TimerKind.Pomodoro);
                _timer.Start();
                break;
            case "hover":   // off: a click opens it, and a click on the character makes it emote
                _settings.ExpandOnHover = arg != "off";
                break;
            case "appsearch":   // appsearch <text> · appsearch off
                if (arg == "off") { await ToggleAppSearch(false); break; }
                AppSearchBox.Text = "";
                await ToggleAppSearch(true);
                await Task.Delay(350);
                foreach (var ch in arg)
                {
                    AppSearchBox.Text += ch;
                    AppSearchBox.CaretIndex = AppSearchBox.Text.Length;
                    await Task.Delay(110 + Random.Shared.Next(80));
                }
                break;
            case "switch":   // the mouse wheel over the pill: next activity
                SwitchLive(1);
                break;
            case "update":   // update 1.4.0: the notice users of an older version get
                ShowToast("", L.F("Доступна версия {0}", arg), L.T("Нажмите, чтобы обновить"), null,
                    (Brush)FindResource("AccentBlue"), ms: 5000);
                break;
            case "fps":   // fps 144 · fps off (the game pill)
                _demoFps = arg == "off" ? null : int.Parse(arg);
                break;
            case "scale":   // scale 120: the island size from Settings > Appearance
                _settings.IslandScale = int.Parse(arg);
                ApplyIslandScale();
                break;
            case "caps":    // caps on · caps off: the Caps Lock notice
                ShowKeyPill("", arg == "on" ? L.T("Caps Lock включён") : L.T("Caps Lock выключен"), null,
                            arg == "on" ? Color.FromRgb(0xFF, 0x9F, 0x0A) : null);
                break;
            case "layout":  // layout EN English: the keyboard layout notice
            {
                var lp = arg.Split(' ', 2);
                ShowKeyPill("", lp[0], lp.Length > 1 ? lp[1] : null, null);
                break;
            }
            case "hide":    // hide on · hide off: as if a program from "Hide over programs" came to the front
                _demoHide = arg == "on";
                break;
            case "behavior":   // the Behavior section, scrolled to the new switches (demo programs in the list)
                _settings.HideInApps = ["Zoom", "Minecraft"];
                _settings.LockPosition = true;
                _pinnedOpen = true;
                TabSettings.IsChecked = true;
                Expand();
                ((System.Windows.Controls.RadioButton)SettingsNav.Children[3]).IsChecked = true;
                await Task.Delay(400);
                RefreshSettingsPage();
                BuildHideAppChips();
                await Task.Delay(200);
                HideAppChips.BringIntoView(new Rect(0, -60, 10, 330));
                break;
            case "notepaste":   // a long text pasted into a new note (all of its lines stay)
                NewNoteBox.Text = arg.Replace("\\n", Environment.NewLine);   // "\n" written as two characters in the command file
                NewNoteBox.CaretIndex = NewNoteBox.Text.Length;
                break;
            case "notesave":
                if (NewNoteBox.Text.Trim().Length > 0)
                {
                    NotesStore.Notes.Insert(0, new Note { Text = NewNoteBox.Text.Trim() });
                    NewNoteBox.Clear();
                    RenderNotes();
                }
                break;
            case "vpnapp":  // the VPN tile without a Windows VPN profile: opens your VPN app
                ShowToast("", L.F("Открываю {0}", arg), null, null, new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)), ms: 2200);
                break;
            case "quit":
                Application.Current.Shutdown();
                break;
        }
    }
}
