using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;

namespace Sufni.App.Tests.Sessions.Models;

public class SessionProcessedGenerationTests
{
    [Fact]
    public void From_CapturesAndSerializesProcessedGeneration()
    {
        var fullTrackId = Guid.NewGuid();
        var session = new Session(Guid.NewGuid(), "session", string.Empty, null)
        {
            DurationSeconds = 12,
            DistanceMeters = 34,
            AscentMeters = 5,
            DescentMeters = 6,
            FullTrack = fullTrackId,
            GpsOffsetSeconds = 1.25,
            Track = [new TrackPoint(100, 1, 2, 3)],
        };

        var generation = SessionProcessedGeneration.From(session);
        session.Track.Clear();
        var roundTrip = AppJson.Deserialize<SessionProcessedGeneration>(
            AppJson.Serialize(generation));

        Assert.NotNull(roundTrip);
        Assert.Equal(12, roundTrip!.DurationSeconds);
        Assert.Equal(34, roundTrip.DistanceMeters);
        Assert.Equal(5, roundTrip.AscentMeters);
        Assert.Equal(6, roundTrip.DescentMeters);
        Assert.Equal(fullTrackId, roundTrip.FullTrackId);
        Assert.Equal(1.25, roundTrip.GpsOffsetSeconds);
        var point = Assert.Single(roundTrip.Track!);
        Assert.Equal(100, point.Time);
        Assert.Equal(1, point.X);
        Assert.Equal(2, point.Y);
        Assert.Equal(3, point.Elevation);
    }
}
