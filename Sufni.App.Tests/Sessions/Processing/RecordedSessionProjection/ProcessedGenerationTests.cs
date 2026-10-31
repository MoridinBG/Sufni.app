using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Sessions.Processing.RecordedSessionProjection;

public class ProcessedGenerationTests
{
    [Fact]
    public void Create_DerivesSummaryMetrics_AndKeepsTelemetryWhenPreparingPersistence()
    {
        var telemetry = TestTelemetryData.CreateMinimal(duration: 3);
        var payload = new ProcessedTelemetryPayload(telemetry, telemetry.BinaryForm, "fingerprint");
        var generation = ProcessedGeneration.Create(payload,
            [new TrackPoint(100, 0, 0, 10), new TrackPoint(101, 3, 4, 14), new TrackPoint(102, 6, 8, 10)]);
        var metadata = TestSnapshots.Session(name: "ride", hasProcessedData: false) with { GpsOffsetSeconds = 1.25 };
        var trackId = Guid.NewGuid();

        var session = generation.CreateSession(metadata, trackId);

        Assert.Equal(3, session.DurationSeconds);
        Assert.InRange(session.DistanceMeters!.Value, 9.98, 10.02);
        Assert.Equal(4, session.AscentMeters);
        Assert.Equal(4, session.DescentMeters);
        Assert.Equal(trackId, session.FullTrack);
        Assert.Equal(1.25, session.GpsOffsetSeconds);
        Assert.Equal(metadata.Name, session.Name);
        Assert.Same(payload.Data, session.ProcessedData);
        Assert.Equal(payload.FingerprintJson, session.ProcessingFingerprintJson);
    }

    [Theory]
    [InlineData(101L, 2.0, 103.0)]
    [InlineData(1000L, 0.0, null)]
    public void WithTrackWindow_UsesAuthoredOriginAndOffset_WithoutChangingOriginalGeneration(
        long timestamp, double offset, double? expectedStart)
    {
        var telemetry = TestTelemetryData.CreateMinimal(duration: 2);
        var original = ProcessedGeneration.Create(new ProcessedTelemetryPayload(telemetry, telemetry.BinaryForm, "fingerprint"), null);
        var metadata = TestSnapshots.Session(timestamp: timestamp) with { GpsOffsetSeconds = offset, DurationSeconds = 999 };
        var track = new Track
        {
            Points = Enumerable.Range(0, 11).Select(index => new TrackPoint(100 + index, index, index, 10)).ToList()
        };

        var scoped = original.WithTrackWindow(track, metadata);

        Assert.Null(original.TrackPoints);
        Assert.Equal(2, scoped.Metrics.DurationSeconds);
        if (expectedStart is { } start)
        {
            Assert.Equal(start, scoped.TrackPoints![0].Time);
            Assert.Equal(start + 2, scoped.TrackPoints[^1].Time);
            Assert.InRange(scoped.Metrics.DistanceMeters!.Value, 2.82, 2.83);
        }
        else
        {
            Assert.Null(scoped.TrackPoints);
            Assert.Null(scoped.Metrics.DistanceMeters);
            Assert.Null(scoped.Metrics.AscentMeters);
            Assert.Null(scoped.Metrics.DescentMeters);
        }
    }
}
