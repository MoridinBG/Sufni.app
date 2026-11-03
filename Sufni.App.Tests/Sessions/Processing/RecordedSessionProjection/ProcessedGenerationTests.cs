using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Sessions.Processing.RecordedSessionProjection;

public class ProcessedGenerationTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CreateSession_DoesNotPersistInvalidDuration(double duration)
    {
        var telemetry = TestTelemetryData.CreateMinimal(duration: duration);
        var generation = ProcessedGeneration.Create(new ProcessedTelemetryPayload(telemetry, [], "fingerprint"), null);

        Assert.Null(generation.CreateSession(TestSnapshots.Session()).DurationSeconds);
    }
}
