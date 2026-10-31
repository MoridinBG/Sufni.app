using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Sessions.Processing.RecordedSessionProjection;

public class ProcessedGenerationTests
{
    [Fact]
    public void Create_PreparesTelemetryAndPoints_WithoutGpsMetrics()
    {
        var telemetry = TestTelemetryData.CreateMinimal(duration: 3);
        var payload = new ProcessedTelemetryPayload(telemetry, telemetry.BinaryForm, "fingerprint");
        var generation = ProcessedGeneration.Create(payload,
            [new TrackPoint(100, 0, 0, 10), new TrackPoint(101, 3, 4, 14), new TrackPoint(102, 6, 8, 10)]);
        var metadata = TestSnapshots.Session(name: "ride", hasProcessedData: false) with { GpsOffsetSeconds = 1.25 };
        var trackId = Guid.NewGuid();

        var session = generation.CreateSession(metadata, trackId);

        Assert.Equal(3, session.DurationSeconds);
        Assert.Null(session.DistanceMeters);
        Assert.Null(session.AscentMeters);
        Assert.Null(session.DescentMeters);
        Assert.Equal(3, generation.TrackPoints!.Count);
        Assert.Equal(trackId, session.FullTrack);
        Assert.Equal(1.25, session.GpsOffsetSeconds);
        Assert.Equal(metadata.Name, session.Name);
        Assert.Same(payload.Data, session.ProcessedData);
        Assert.Equal(payload.FingerprintJson, session.ProcessingFingerprintJson);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CreateSession_DoesNotPersistInvalidDuration(double duration)
    {
        var telemetry = TestTelemetryData.CreateMinimal(duration: duration);
        var generation = ProcessedGeneration.Create(new ProcessedTelemetryPayload(telemetry, [], "fingerprint"), null);

        Assert.Null(generation.CreateSession(TestSnapshots.Session(), null).DurationSeconds);
    }
}
