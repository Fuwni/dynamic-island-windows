using System.Buffers.Binary;
using System.Text;
using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class AimpRemoteTests
{
    private static byte[] BuildRemoteBuffer(
        bool active,
        uint durationMs,
        string album,
        string artist,
        string date,
        string fileName,
        string genre,
        string title)
    {
        var buf = new byte[2048];
        var span = buf.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span[4..8], active ? 1 : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..20], durationMs);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..44], (uint)album.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[44..48], (uint)artist.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[48..52], (uint)date.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[52..56], (uint)fileName.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[56..60], (uint)genre.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[60..64], (uint)title.Length);

        int offset = 88;
        foreach (var s in new[] { album, artist, date, fileName, genre, title })
        {
            if (s.Length == 0) continue;
            offset += Encoding.Unicode.GetBytes(s, span[offset..]);
        }
        return buf;
    }

    [Fact]
    public void ParsesRemoteFileInfoStringsAndDuration()
    {
        var buf = BuildRemoteBuffer(
            active: true,
            durationMs: 215400,
            album: "Млечный путь",
            artist: "Кино",
            date: "1988",
            fileName: @"D:\Music\Kino - Gruppa Krovi.flac",
            genre: "Rock",
            title: "Группа крови");

        var info = AimpRemote.ParseRemoteFileInfo(buf);
        Assert.NotNull(info);
        Assert.True(info!.Active);
        Assert.Equal("Группа крови", info.Title);
        Assert.Equal("Кино", info.Artist);
        Assert.Equal("Млечный путь", info.Album);
        Assert.Equal(@"D:\Music\Kino - Gruppa Krovi.flac", info.FileName);
        Assert.Equal(215400u, info.Duration);
    }

    [Fact]
    public void FallsBackToFileNameWhenTitleMissing()
    {
        var buf = BuildRemoteBuffer(
            active: true,
            durationMs: 120000,
            album: "",
            artist: "Artist",
            date: "",
            fileName: @"C:\Tracks\Demo Track.mp3",
            genre: "",
            title: "");

        var info = AimpRemote.ParseRemoteFileInfo(buf);
        Assert.NotNull(info);
        Assert.Equal("Demo Track", info!.Title);
    }

    [Fact]
    public void HandlesInactiveAndShortBuffersSafely()
    {
        Assert.Null(AimpRemote.ParseRemoteFileInfo(new byte[16]));

        var inactive = BuildRemoteBuffer(false, 0, "", "", "", "", "", "");
        var info = AimpRemote.ParseRemoteFileInfo(inactive);
        Assert.NotNull(info);
        Assert.False(info!.Active);
    }
}
