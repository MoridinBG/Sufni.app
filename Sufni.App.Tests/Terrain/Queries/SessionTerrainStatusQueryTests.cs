using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Queries;

public class SessionTerrainStatusQueryTests
{
    [Fact]
    public void ComputedProfileFollowsRecordingAndSelectedTrackAcrossMetadataEdits()
    {
        var session = TestSnapshots.Session(hasProcessedData: true) with { Timestamp = 100, DurationSeconds = 20 };
        var track = new TrackSnapshot(Guid.NewGuid(), 100, 120, 7);
        var summary = new TerrainSummary(10, null);
        session = TestSnapshots.WithCurrentTerrain(session, TerrainOutcome.Computed, summary, [track]);

        Assert.Equal(new TerrainStatus.Current(session.TerrainProfileKey!, summary),
            SessionTerrainStatusQuery.Evaluate(session with { Name = "renamed" }, [track]).Status);
        foreach (var changed in new[] {
            session with { ProcessedTelemetryRevision = session.ProcessedTelemetryRevision + 1 },
            session with { GpsOffsetSeconds = 2 }, session with { Timestamp = 101 },
            session with { DurationSeconds = 19 }, session with { WindowStartSeconds = 0.25 } })
            Assert.IsType<TerrainStatus.Pending>(SessionTerrainStatusQuery.Evaluate(changed, [track]).Status);
        foreach (var changed in new[] { track with { PointsRevision = 8 }, track with { Id = Guid.NewGuid() } })
            Assert.IsType<TerrainStatus.Pending>(SessionTerrainStatusQuery.Evaluate(session, [changed]).Status);
    }

    [Theory]
    [InlineData(TerrainOutcome.NoTrack, TerrainAvailabilityReason.NoTrack)]
    [InlineData(TerrainOutcome.NoMovement, TerrainAvailabilityReason.NoMovement)]
    [InlineData(TerrainOutcome.ContentMissing, TerrainAvailabilityReason.ContentMissing)]
    [InlineData(TerrainOutcome.Failed, null)]
    public void TerminalOutcomesExpireWithTheirKey(TerrainOutcome outcome, TerrainAvailabilityReason? reason)
    {
        var session = TestSnapshots.Session(hasProcessedData: true);
        session = TestSnapshots.WithCurrentTerrain(session, outcome);
        var key = session.TerrainProfileKey!;
        var status = SessionTerrainStatusQuery.Evaluate(session, []).Status;
        if (reason is { } unavailable)
            Assert.Equal(new TerrainStatus.Unavailable(unavailable), status);
        else
            Assert.IsType<TerrainStatus.Failed>(status);
        Assert.IsType<TerrainStatus.Pending>(SessionTerrainStatusQuery.Evaluate(
            session with { TerrainProfileKey = key with { AlgorithmVersion = key.AlgorithmVersion - 1 } }, []).Status);
        Assert.Equal(new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoTelemetry),
            SessionTerrainStatusQuery.Evaluate(session with { HasProcessedData = false }, []).Status);
    }
    [Fact]
    public void ClassSummaryStoredUnderTheCurrentProfileAndScaleIsClassified()
    {
        var (session, status) = Current();
        var summary = new TerrainClassSummary(400, 0.25);
        var classified = session with { TerrainClassKey = ClassKey(status), TerrainClassSummary = summary };
        Assert.Equal(new TerrainClassState.Classified(summary),
            SessionTerrainStatusQuery.EvaluateClasses(classified, status, Scale));
    }

    [Fact]
    public void MissingOrOutdatedClassKeyIsPending()
    {
        var (session, status) = Current();
        var summary = new TerrainClassSummary(400, 0.25);
        var key = ClassKey(status);
        foreach (var stored in new TerrainClassKey?[] { null, key with { Scale = Scale with { FrameImu = 1 } },
                     key with { ClassifierVersion = key.ClassifierVersion - 1 },
                     key with { Profile = key.Profile with { AlgorithmVersion = key.Profile.AlgorithmVersion - 1 } } })
            Assert.IsType<TerrainClassState.Pending>(SessionTerrainStatusQuery.EvaluateClasses(
                session with { TerrainClassKey = stored, TerrainClassSummary = summary }, status, Scale));
    }

    [Fact]
    public void ClassesAreUnavailableWithoutACurrentProfileOrAReadyScale()
    {
        var (session, status) = Current();
        var classified = session with { TerrainClassKey = ClassKey(status), TerrainClassSummary = new(400, 0.25) };
        Assert.IsType<TerrainClassState.Unavailable>(SessionTerrainStatusQuery.EvaluateClasses(classified, status, null));
        Assert.IsType<TerrainClassState.Unavailable>(
            SessionTerrainStatusQuery.EvaluateClasses(classified, status, TerrainScaleKey.None));
        Assert.IsType<TerrainClassState.Unavailable>(
            SessionTerrainStatusQuery.EvaluateClasses(classified, new TerrainStatus.Pending(), Scale));
    }

    private static readonly TerrainScaleKey Scale = new(0, null, null, null);

    private static (SessionSnapshot Session, TerrainStatus.Current Status) Current()
    {
        var session = TestSnapshots.WithCurrentTerrain(TestSnapshots.Session(hasProcessedData: true),
            TerrainOutcome.Computed, new TerrainSummary(10, null));
        return (session, Assert.IsType<TerrainStatus.Current>(SessionTerrainStatusQuery.Evaluate(session, []).Status));
    }

    private static TerrainClassKey ClassKey(TerrainStatus.Current status) => new(status.Key, Scale, TerrainClassifier.Version);
}
