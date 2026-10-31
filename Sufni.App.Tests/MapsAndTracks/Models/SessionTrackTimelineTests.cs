using Sufni.App.Sessions.Models;
using Sufni.App.Video.Models;
using Sufni.App.Video.Stores;

namespace Sufni.App.Tests.MapsAndTracks.Models;

public class SessionTrackTimelineTests
{
    [Theory]
    [InlineData(2.5, 1007.5)]
    [InlineData(-2.5, 1002.5)]
    public void Mapping_RetainsFractionalOriginAndAlignment(double offset, double trackSeconds)
    {
        var timeline = new SessionTrackTimeline(1000.25, offset);

        Assert.Equal(trackSeconds, timeline.ToTrackSeconds(4.75));
        Assert.Equal(4.75, timeline.ToTelemetrySeconds(trackSeconds));
    }

    [Fact]
    public void VideoContext_UsesCaptureClockAndClipAlignment_IndependentlyOfGpsAlignment()
    {
        var clip = new SessionVideoSnapshot(Guid.NewGuid(), Guid.NewGuid(), "/clip.mp4", 990, 30, -1.5, 0, true);
        var context = VideoPlaybackContext.FromTimeline(clip, new SessionTrackTimeline(1000.25, 7), 20);

        Assert.Equal(13.75, context.ToVideoSeconds(5));
        Assert.Equal(5, context.ToTelemetrySeconds(13.75));
        Assert.True(context.CanSync);
    }
}
