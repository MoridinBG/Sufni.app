using System.Text.Json;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;

namespace Sufni.App.Tests.Sessions.Models;

public class SessionProcessedGenerationTests
{
    [Fact]
    public void From_CapturesGenerationWithoutAuthoredAlignment()
    {
        var session = new Session(Guid.NewGuid(), "session", string.Empty, null)
        {
            DurationSeconds = 12,
            DistanceMeters = 34,
            AscentMeters = 5,
            DescentMeters = 6,

            GpsOffsetSeconds = 1.25,

        };

        var generation = SessionProcessedGeneration.From(session);
        var json = AppJson.Serialize(generation);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("GpsOffsetSeconds", out _));
        var roundTrip = AppJson.Deserialize<SessionProcessedGeneration>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(12, roundTrip!.DurationSeconds);
        Assert.False(document.RootElement.TryGetProperty("FullTrackId", out _));
        Assert.False(document.RootElement.TryGetProperty("Track", out _));
        Assert.False(document.RootElement.TryGetProperty("DistanceMeters", out _));
        Assert.False(document.RootElement.TryGetProperty("AscentMeters", out _));
        Assert.False(document.RootElement.TryGetProperty("DescentMeters", out _));
        using var sessionJson = JsonDocument.Parse(AppJson.Serialize(session));
        Assert.False(sessionJson.RootElement.TryGetProperty("track", out _));
        Assert.False(sessionJson.RootElement.TryGetProperty("full_track_id", out _));
    }
}
