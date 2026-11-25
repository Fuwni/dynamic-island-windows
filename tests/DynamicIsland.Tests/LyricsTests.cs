using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class LrcParserTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void ParsesStampsAndText()
    {
        var lines = LyricsService.ParseLrc("[00:12.50]Hello\n[01:02.03] World  \n");
        Assert.Equal(2, lines.Count);
        Assert.Equal(S(12.5), lines[0].Time);
        Assert.Equal("Hello", lines[0].Text);
        Assert.Equal(S(62.03), lines[1].Time);
        Assert.Equal("World", lines[1].Text);
    }

    [Fact]
    public void AcceptsColonCentisecondsAndWholeSeconds()
    {
        var lines = LyricsService.ParseLrc("[00:05:25]a\n[00:07]b");
        Assert.Equal(S(5.25), lines[0].Time);
        Assert.Equal(S(7), lines[1].Time);
    }

    [Fact]
    public void SeveralStampsOnOneLineRepeatIt_AndAllIsSorted()
    {
        var lines = LyricsService.ParseLrc("[00:30.00]chorus\n[00:10.00][00:50.00]refrain\n");
        Assert.Equal([S(10), S(30), S(50)], lines.Select(l => l.Time));
        Assert.Equal(["refrain", "chorus", "refrain"], lines.Select(l => l.Text));
    }

    [Fact]
    public void SkipsTagsAndUntimedLines_KeepsEmptyTimedLines()
    {
        var lines = LyricsService.ParseLrc("[ar:Someone]\n[ti:Song]\nno stamp here\n[00:01.00]\n[00:02.00]x\r\n");
        Assert.Equal(2, lines.Count);
        Assert.Equal("", lines[0].Text);   // an instrumental gap
        Assert.Equal("x", lines[1].Text);  // \r is trimmed
    }

    [Fact]
    public void OffsetTagMovesLinesEarlierOrLater()
    {
        var earlier = LyricsService.ParseLrc("[offset:+500]\n[00:10.00]a");
        Assert.Equal(S(9.5), earlier[0].Time);
        var later = LyricsService.ParseLrc("[offset:-250]\n[00:10.00]a");
        Assert.Equal(S(10.25), later[0].Time);
        var clamped = LyricsService.ParseLrc("[offset:1000]\n[00:00.20]a");
        Assert.Equal(TimeSpan.Zero, clamped[0].Time);
    }

    [Fact]
    public void EmptyOrGarbageGivesNothing()
    {
        Assert.Empty(LyricsService.ParseLrc(""));
        Assert.Empty(LyricsService.ParseLrc("just words\nmore words"));
    }

    [Fact]
    public void LineAtFindsTheLineBeingSung()
    {
        var lines = LyricsService.ParseLrc("[00:10.00]a\n[00:20.00]b\n[00:30.00]c");
        Assert.Equal(-1, LyricsService.LineAt(lines, S(5)));
        Assert.Equal(0, LyricsService.LineAt(lines, S(10)));
        Assert.Equal(0, LyricsService.LineAt(lines, S(19.99)));
        Assert.Equal(1, LyricsService.LineAt(lines, S(20)));
        Assert.Equal(2, LyricsService.LineAt(lines, S(500)));
        Assert.Equal(-1, LyricsService.LineAt([], S(5)));
    }

    [Fact]
    public void PickBestKeepsWithinTwoSecondsAndPrefersSynced()
    {
        var items = new List<LyricsCandidate>
        {
            new(180, Synced: true, Plain: true, Instrumental: false),   // wrong length (a remix)
            new(211, Synced: false, Plain: true, Instrumental: false),
            new(213.4, Synced: true, Plain: true, Instrumental: false),
        };
        Assert.Equal(2, LyricsService.PickBest(items, 212));
        Assert.Equal(-1, LyricsService.PickBest(items, 300));
        // length unknown: anything goes, synced first
        Assert.Equal(0, LyricsService.PickBest(items, 0));
        // words before "instrumental"
        Assert.Equal(1, LyricsService.PickBest([new(200, false, false, true), new(200, false, true, false)], 200));
        Assert.Equal(0, LyricsService.PickBest([new(200, false, false, true)], 200));
        Assert.Equal(-1, LyricsService.PickBest([new(200, false, false, false)], 200));
    }
}

public class PlaybackClockTests
{
    private const double F = 1000;   // 1 tick = 1 ms
    private static long T(double s) => (long)(s * F);
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void RunsOnItsOwnBetweenReports()
    {
        var c = new PlaybackClock(F);
        c.Report("a", S(10), true, T(0));
        Assert.Equal(S(12.5), c.At(T(2.5)));
    }

    [Fact]
    public void IgnoresSmallJitterButTakesBigDrift()
    {
        var c = new PlaybackClock(F);
        c.Report("a", S(10), true, T(0));
        c.Report("a", S(12.2), true, T(2));    // 200 ms off: the player's jitter, ignored
        Assert.Equal(1, c.Resyncs);
        Assert.Equal(S(13), c.At(T(3)));
        c.Report("a", S(14.5), true, T(4));    // 500 ms off: taken
        Assert.Equal(2, c.Resyncs);
        Assert.Equal(S(15.5), c.At(T(5)));
    }

    [Fact]
    public void StopsOnPauseAndResyncsOnTrackChange()
    {
        var c = new PlaybackClock(F);
        c.Report("a", S(10), true, T(0));
        c.Report("a", S(11), false, T(1));     // paused
        Assert.Equal(S(11), c.At(T(60)));
        c.Report("b", S(0.1), true, T(60));    // the next song, even though the times are close
        Assert.Equal(S(1.1), c.At(T(61)));
        Assert.Equal(3, c.Resyncs);
    }

    [Fact]
    public void ASeekHoldsUntilThePlayerCatchesUp()
    {
        var c = new PlaybackClock(F);
        c.Report("a", S(10), true, T(0));
        c.Seeked(S(100), T(1));
        c.Report("a", S(11.3), true, T(1.3));  // the player still says the old place: ignored
        Assert.Equal(S(100.5), c.At(T(1.5)));
        c.Report("a", S(100.6), true, T(1.6)); // caught up
        Assert.Equal(S(101), c.At(T(2)));
        c.Report("a", S(50), true, T(2.2));    // after that, a real jump is taken at once
        Assert.Equal(S(50), c.At(T(2.2)));
    }

    [Fact]
    public void TheHoldEndsAfterTwoSeconds()
    {
        var c = new PlaybackClock(F);
        c.Report("a", S(10), true, T(0));
        c.Seeked(S(100), T(1));                // the player refused the seek
        c.Report("a", S(13.5), true, T(3.5));
        Assert.Equal(S(13.5), c.At(T(3.5)));
    }
}

public class SourceLeadTests
{
    [Theory]
    [InlineData("Chrome._crx_cinhimbnkkghhklpknlkffjgod", 500)]
    [InlineData("chrome.exe", 500)]
    [InlineData("MSEdge", 500)]
    [InlineData("308046B0AF4A39CB", 500)]
    [InlineData("Spotify.exe", 0)]
    [InlineData("A025C540.Yandex.Music_vfvw9svesycw6!App", 0)]
    [InlineData("", 0)]
    public void BrowsersGetALead(string appId, int lead) => Assert.Equal(lead, DynamicIsland.Services.PlaybackClock.SourceLeadMs(appId));
}

public class TitleCleaningTests
{
    private static readonly string Fire = char.ConvertFromUtf32(0x1F525);

    [Theory]
    [InlineData("Баста", "Сансара (prod. by Beatz)", "Баста", "Сансара")]
    [InlineData("Артист", "Песня [Explicit]", "Артист", "Песня")]
    [InlineData("Артист", "Песня (OST Сериал)", "Артист", "Песня")]
    [InlineData("Артист", "Песня (из к/ф Брат)", "Артист", "Песня")]
    [InlineData("Канал", "Би-2 — Полковнику никто не пишет", "Би-2", "Полковнику никто не пишет")]
    [InlineData("Канал", "Artist – Song (Official Video)", "Artist", "Song")]
    [InlineData("Artist", "Song (feat. Other)", "Artist", "Song")]
    [InlineData("Artist - Topic", "Song - Remastered 2011", "Artist", "Song")]
    [InlineData("Artist", "Song (Prodigy Cover)", "Artist", "Song (Prodigy Cover)")]   // "prod" only as a word
    [InlineData("Artist", "Love (Ostrich)", "Artist", "Love (Ostrich)")]
    public void CleansWhatPlayersAdd(string artist, string title, string wantArtist, string wantTitle)
    {
        var (a, t) = DynamicIsland.Services.LyricsService.Clean(artist, title);
        Assert.Equal(wantArtist, a);
        Assert.Equal(wantTitle, t);
    }

    [Fact]
    public void DropsEmoji()
    {
        var (a, t) = DynamicIsland.Services.LyricsService.Clean("Artist " + Fire, Fire + " Song " + Fire);
        Assert.Equal("Artist", a);
        Assert.Equal("Song", t);
    }

    [Theory]
    [InlineData("Би-2", "би 2", true)]
    [InlineData("Ёлка", "Елка", true)]
    [InlineData("Баста", "Баста, Скриптонит", true)]
    [InlineData("Баста", "Каста", false)]
    [InlineData("", "Баста", false)]
    public void ComparesArtists(string a, string b, bool same) => Assert.Equal(same, DynamicIsland.Services.LyricsService.SameArtist(a, b));

    [Fact]
    public void TitleOnlySearchNeedsTheArtist()
    {
        var items = new List<DynamicIsland.Services.LyricsCandidate>
        {
            new(200, true, true, false, "Someone Else"),
            new(201, true, true, false, "Би-2"),
        };
        Assert.Equal(1, DynamicIsland.Services.LyricsService.PickBest(items, 200, "би 2"));
        Assert.Equal(-1, DynamicIsland.Services.LyricsService.PickBest(items, 200, "Сплин"));
        Assert.Equal(0, DynamicIsland.Services.LyricsService.PickBest(items, 200));
    }
}

public class KaraokeTimingTests
{
    private static double[] W(string s) => DynamicIsland.Views.KaraokeTiming.Weights(s);

    [Fact]
    public void DashBetweenWordsIsAPause_HyphenInAWordIsNot()
    {
        Assert.Equal(6, W("Я иду — и вдруг")[6]);
        Assert.Equal(6, W("Я иду – и вдруг")[6]);
        Assert.Equal(6, W("Я иду - и вдруг")[6]);
        Assert.Equal(1, W("кто-то")[3]);
        Assert.Equal(3, W("Да, нет")[2]);
    }

    [Fact]
    public void TheLightWaitsAtTheDash()
    {
        var text = "Я иду — и вдруг";
        var w = W(text);
        int dash = text.IndexOf('—');
        // the share of time when the light reaches the dash
        double before = w.Take(dash).Sum() / w.Sum(), after = w.Take(dash + 1).Sum() / w.Sum();
        Assert.True(after - before > 0.2, "the dash takes a real share of the line's time");
        // all through the pause the light stays right after the dash, not in the next words
        foreach (var p in new[] { before + 0.01, (before + after) / 2, after - 0.001 })
            Assert.Equal(dash + 1, DynamicIsland.Views.KaraokeTiming.CharAt(w, p));
        Assert.True(DynamicIsland.Views.KaraokeTiming.CharAt(w, after + 0.05) > dash + 1);
    }

    [Fact]
    public void EvenLineRunsEvenly()
    {
        var w = W("abcdef");
        Assert.Equal(3, DynamicIsland.Views.KaraokeTiming.CharAt(w, 0.5), 6);
        Assert.Equal(0, DynamicIsland.Views.KaraokeTiming.CharAt(w, 0));
        Assert.Equal(6, DynamicIsland.Views.KaraokeTiming.CharAt(w, 1));
        Assert.Equal(0, DynamicIsland.Views.KaraokeTiming.CharAt([], 0.5));
    }

    [Fact]
    public void TheLightFillsTheTimeToTheNextLine()
    {
        const string line = "Tryin' to hold on, d-didn't even know";
        // sung over 7 s: the light takes almost all of it, not the 5 s a fixed letter speed would give
        Assert.Equal(6.3, DynamicIsland.Views.KaraokeTiming.Duration(line, 7), 6);
        // a 40 s instrumental gap after it: capped to slow singing
        Assert.True(DynamicIsland.Views.KaraokeTiming.Duration(line, 40) < 14);
        // a quick line: never faster than 0.8 s
        Assert.Equal(0.8, DynamicIsland.Views.KaraokeTiming.Duration("Hey", 0.5), 6);
        // no next line: the natural speed
        Assert.Equal(DynamicIsland.Views.KaraokeTiming.Natural(line), DynamicIsland.Views.KaraokeTiming.Duration(line, 0), 6);
        // a hyphen inside a word is not a pause
        Assert.Equal(1, DynamicIsland.Views.KaraokeTiming.Weights(line)[line.IndexOf('-')]);
    }

    [Fact]
    public void PausesMakeTheLineLonger()
    {
        Assert.True(DynamicIsland.Views.KaraokeTiming.Natural("Я иду — и вдруг") > DynamicIsland.Views.KaraokeTiming.Natural("Я иду и вдруг") + 0.5);
    }
}

public class KaraokeLightTests
{
    [Fact]
    public void FullWidthMarksArePauses()
    {
        var w = DynamicIsland.Views.KaraokeTiming.Weights("你好，世界。走、来！吗？说：是；嗯…⋯");
        foreach (char c in "，。、：；") Assert.Equal(3, w["你好，世界。走、来！吗？说：是；嗯…⋯".IndexOf(c)]);
        Assert.Equal(2, w[9]);
        Assert.Equal(2, w[11]);
        Assert.Equal(5, w[^1]);
        Assert.Equal(5, w[^2]);
        Assert.Equal(1, w[0]);
    }

    public static TheoryData<string> Lines => new()
    {
        "Я иду по улице одна",
        "Tryin' to hold on, didn't even know",
        "我们一起走过的那些日子",
        "君の名前を呼んでいたカタカナ",
        "너를 사랑해 영원히",
        "Love 你 forever 永遠に 사랑 и всё",
    };

    /// <summary>
    /// Every script lights up the same way: the ink left of the light is bright and the ink right of it dim.
    /// The line is drawn by the view's own brush and the columns of the picture are measured.
    /// </summary>
    [Theory]
    [MemberData(nameof(Lines))]
    public void TheLightIsOneEdgeAcrossTheWholeLine(string text)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { Check(text); } catch (Exception e) { error = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
    }

    private static void Check(string text)
    {
        var face = new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"), System.Windows.FontStyles.Normal, System.Windows.FontWeights.SemiBold, System.Windows.FontStretches.Normal);
        var line = new System.Windows.Media.FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, face, 26, System.Windows.Media.Brushes.White, 1);
        const double x0 = 20, p = 0.5;
        var black = System.Windows.Media.Colors.Black;
        var dim = System.Windows.Media.Color.FromArgb(255, 70, 70, 70);
        line.SetForegroundBrush(DynamicIsland.Views.KaraokeView.SungBrush(System.Windows.Media.Colors.White, System.Windows.Media.Colors.White, dim, p, x0, line.Width));
        int w = (int)Math.Ceiling(line.Width + 2 * x0), h = (int)Math.Ceiling(line.Height + 10);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(black), null, new System.Windows.Rect(0, 0, w, h));
            dc.DrawText(line, new System.Windows.Point(x0, 5));
        }
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(visual);
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        // the brightest ink in each column
        int Column(int x) { int m = 0; for (int y = 0; y < h; y++) m = Math.Max(m, (int)px[(y * w + x) * 4 + 2]); return m; }
        double edge = x0 + line.Width * p, band = line.Width * 0.1;
        int litMax = 0, darkMax = 0;
        for (int x = (int)x0; x < (int)(edge - band); x++) litMax = Math.Max(litMax, Column(x));
        for (int x = (int)(edge + band); x < (int)(x0 + line.Width); x++) darkMax = Math.Max(darkMax, Column(x));
        // full brightness reached on the left; on the right nothing brighter than the dim colour
        Assert.True(litMax > 200, $"lit part reaches {litMax}");
        Assert.True(darkMax <= 80, $"unsung part reaches {darkMax} (a gradient restarted inside the line)");
    }
}

public class NetEasePickTests
{
    private static readonly TimeSpan Len = TimeSpan.FromSeconds(164);

    [Fact]
    public void RequiresTheSameArtist()
    {
        // the title matches, the artist does not: someone else's words must never be shown
        LyricsService.NetEaseSong[] songs = [new(1, "Sayonara", "Another Band", 164)];
        Assert.Equal(0, LyricsService.PickNetEase(songs, "Enemy Inside", "Sayonara", Len));
    }

    [Fact]
    public void TakesTheRightTrack()
    {
        LyricsService.NetEaseSong[] songs =
        [
            new(1, "Sayonara", "Another Band", 164),
            new(2, "Sayonara", "Enemy Inside", 164),
        ];
        Assert.Equal(2, LyricsService.PickNetEase(songs, "Enemy Inside", "Sayonara", Len));
    }

    [Fact]
    public void SkipsAnotherVersionOfTheRightTrack()
    {
        // same artist and title, but a live version a minute longer: it would be out of sync
        LyricsService.NetEaseSong[] songs = [new(3, "Sayonara", "Enemy Inside", 290)];
        Assert.Equal(0, LyricsService.PickNetEase(songs, "Enemy Inside", "Sayonara", Len));
    }

    [Fact]
    public void LengthIsIgnoredWhenUnknown()
    {
        LyricsService.NetEaseSong[] songs = [new(4, "Sayonara", "Enemy Inside", 290)];
        Assert.Equal(4, LyricsService.PickNetEase(songs, "Enemy Inside", "Sayonara", TimeSpan.Zero));
    }
}
