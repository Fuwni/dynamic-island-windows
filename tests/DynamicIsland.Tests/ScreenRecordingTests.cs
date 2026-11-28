using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class ScreenRecordingTests
{
    [Fact]
    public void ADisconnectedDisplayDoesNotStartARecording()
    {
        var recording = new ScreenRecording();
        bool finished = false;
        recording.Finished += (_, _) => finished = true;

        Assert.Throws<InvalidOperationException>(() =>
            recording.Start(systemAudio: false, microphone: false, displayDevice: "missing-display-" + Guid.NewGuid()));

        Assert.False(recording.IsRecording);
        Assert.Equal(default, recording.StartedAt);
        Assert.False(finished);
    }
}
