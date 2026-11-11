using System.Diagnostics;

namespace DynamicIsland.Services;

/// <summary>
/// AIMP and Winamp don't always report to Windows media controls,
/// so we read them directly from their windows and control them via their remote APIs.
/// </summary>
public static class LegacyPlayers
{
    public sealed record Track(string App, string Artist, string Title, bool Playing);

    // Winamp IPC
    private const int IPC_ISPLAYING = 104;
    private const int WA_PREV = 40044, WA_PLAY = 40045, WA_PAUSE = 40046, WA_NEXT = 40048;

    // AIMP remote API (apiRemote.h)
    private const int AIMP_RA_WM_COMMAND = Native.WM_USER + 0x75;
    private const int AIMP_PLAYPAUSE = 14, AIMP_NEXT = 17, AIMP_PREV = 18;

    private static IntPtr Winamp => Native.FindWindow("Winamp v1.x", null);
    private static IntPtr AimpRemote => Native.FindWindow("AIMP2_RemoteInfo", "AIMP2_RemoteInfo");

    public static Track? Detect()
    {
        var wa = Winamp;
        if (wa != IntPtr.Zero)
        {
            var title = Native.WindowText(wa);
            var state = (int)Native.SendMessage(wa, Native.WM_USER, 0, IPC_ISPLAYING);
            title = System.Text.RegularExpressions.Regex.Replace(title, @"^\d+\.\s*", "");
            title = title.Replace(" - Winamp", "").Replace("[Paused]", "").Replace("[Stopped]", "").Trim();
            if (title.Length > 0 && !title.Equals("Winamp", StringComparison.OrdinalIgnoreCase))
            {
                var (artist, song) = Split(title);
                return new Track("Winamp", artist, song, state == 1);
            }
        }

        var aimp = Process.GetProcessesByName("AIMP").FirstOrDefault();
        if (aimp != null)
        {
            var title = aimp.MainWindowTitle.Trim();
            if (title.Length > 0 && !title.StartsWith("AIMP", StringComparison.OrdinalIgnoreCase))
            {
                var (artist, song) = Split(title);
                return new Track("AIMP", artist, song, true);
            }
        }
        return null;
    }

    private static (string, string) Split(string s)
    {
        var i = s.IndexOf(" - ", StringComparison.Ordinal);
        return i > 0 ? (s[..i].Trim(), s[(i + 3)..].Trim()) : ("", s);
    }

    public static void PlayPause(Track t)
    {
        if (t.App == "Winamp" && Winamp != IntPtr.Zero)
            Native.SendMessage(Winamp, Native.WM_COMMAND, t.Playing ? WA_PAUSE : WA_PLAY, 0);
        else if (t.App == "AIMP" && AimpRemote != IntPtr.Zero)
            Native.SendMessage(AimpRemote, AIMP_RA_WM_COMMAND, AIMP_PLAYPAUSE, 0);
        else
            Native.PressKey(0xB3); // VK_MEDIA_PLAY_PAUSE
    }

    public static void Next(Track t)
    {
        if (t.App == "Winamp" && Winamp != IntPtr.Zero)
            Native.SendMessage(Winamp, Native.WM_COMMAND, WA_NEXT, 0);
        else if (t.App == "AIMP" && AimpRemote != IntPtr.Zero)
            Native.SendMessage(AimpRemote, AIMP_RA_WM_COMMAND, AIMP_NEXT, 0);
        else
            Native.PressKey(0xB0); // VK_MEDIA_NEXT_TRACK
    }

    public static void Previous(Track t)
    {
        if (t.App == "Winamp" && Winamp != IntPtr.Zero)
            Native.SendMessage(Winamp, Native.WM_COMMAND, WA_PREV, 0);
        else if (t.App == "AIMP" && AimpRemote != IntPtr.Zero)
            Native.SendMessage(AimpRemote, AIMP_RA_WM_COMMAND, AIMP_PREV, 0);
        else
            Native.PressKey(0xB1); // VK_MEDIA_PREV_TRACK
    }
}
