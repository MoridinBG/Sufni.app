using System.Collections.Immutable;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
using Sufni.App.Terrain.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Segments.Coordinators;

public partial class SegmentCoordinatorTests
{
    private static readonly Lazy<TerrainProfile> earlierRide = new(() => SyntheticTrailRuns.Record(8, roughRebound: 0.60));
    private static readonly Lazy<TerrainProfile> currentRide = new(() => SyntheticTrailRuns.Record(8, calmSpeedFactor: 0.8));

    [Fact]
    public async Task MatchInsideBaselineDoesNotInfluenceQualifiedPairRatios()
    {
        var (source, match) = TerrainComparisonInputs(5);
        var excluding = await CompareTerrain(source, match);
        var including = await CompareTerrain(IncludeMatch(source, match), match);
        var expected = Assert.IsType<SegmentTerrainComparison.Compared>(excluding).Rows.Single(r => r.Class is null);
        var actual = Assert.IsType<SegmentTerrainComparison.Compared>(including).Rows.Single(r => r.Class is null);
        Assert.All(expected.Responses, entry =>
        {
            Assert.NotNull(entry.Value.Ratio);
            Assert.Equal(entry.Value.Ratio, actual.Responses[entry.Key].Ratio);
        });
    }

    [Fact]
    public async Task ComparedRideCannotSupplyFifthBaselineSession()
    {
        var (source, match) = TerrainComparisonInputs(4);
        var result = await CompareTerrain(IncludeMatch(source, match), match);
        var all = Assert.IsType<SegmentTerrainComparison.Compared>(result).Rows.Single(r => r.Class is null);
        Assert.All(all.Responses.Values, cell => Assert.Equal(ResponseCellState.NoBaseline, cell.State));
        Assert.NotNull(all.Pace);
        Assert.NotNull(all.FrontSamePosition.Ratio);
        Assert.NotNull(all.RearSamePosition.Ratio);
    }

    [Fact]
    public async Task MatchWithOlderProfileHasNoMatchProfile()
    {
        var (source, match) = TerrainComparisonInputs(0);
        var key = TerrainProfileKey.From(match.ContentToken);
        terrainProfiles.Seed(match.SessionId, key with { AlgorithmVersion = key.AlgorithmVersion - 1 }, earlierRide.Value);
        var result = await CompareTerrain(source, match);
        Assert.Equal(SegmentTerrainUnavailable.NoMatchProfile, Assert.IsType<SegmentTerrainComparison.Unavailable>(result).Reason);
    }

    [Fact]
    public async Task RidesWithDisjointImusHaveNoCommonSensor()
    {
        var (source, match) = TerrainComparisonInputs(0, SyntheticTrailRuns.Record(8, fork: false),
            SyntheticTrailRuns.Record(8, frame: false));
        var result = await CompareTerrain(source, match);
        Assert.Equal(SegmentTerrainUnavailable.NoCommonSensor, Assert.IsType<SegmentTerrainComparison.Unavailable>(result).Reason);
    }

    [Fact]
    public async Task ProfileReadFailureReturnsFailedTerrainComparison()
    {
        var (source, match) = TerrainComparisonInputs(0);
        terrainProfiles.ReadFailure = new IOException("profile read failed");
        Assert.IsType<SegmentTerrainComparison.Failed>(await CompareTerrain(source, match));
    }

    [Fact]
    public async Task TrailAverageClassesEveryMatchedWindowAndNamesItsRunCount()
    {
        var (source, match) = TerrainComparisonInputs(0);
        var allRough = new TrailAverage.Averaged(4, [.. source.Classified.Profile.Windows.Select((w, i) =>
            new TrailAverageWindow(i, w.StartSeconds, w.EndSeconds, null, null, null, null, RoughnessClass.Rough, null))]);
        var compared = Assert.IsType<SegmentTerrainComparison.Compared>(await CompareTerrain(source, match, allRough));
        Assert.Equal(4, compared.AverageRunCount);
        var all = compared.Rows.Single(r => r.Class is null);
        Assert.True(all.DistanceMetres > 0);
        Assert.Equal(all.DistanceMetres, compared.Rows.Single(r => r.Class == RoughnessClass.Rough).DistanceMetres);
        Assert.All(compared.Rows.Where(r => r.Class is RoughnessClass.Calm or RoughnessClass.Medium), r => Assert.Equal(0, r.DistanceMetres));
    }

    [Fact]
    public async Task TrailAverageCountsOnlyMatchesWithCurrentAlignedProfiles()
    {
        var (source, first) = TerrainComparisonInputs(0);
        var repeated = SyntheticTrailRuns.Record(8, recordingSeed: 40);
        var second = TestSegmentMatches.OnTrail(source.Classified.Profile, repeated);
        terrainProfiles.Seed(second.SessionId, source.Key, repeated);
        var unrelated = SyntheticTrailRuns.Record(81);
        var third = TestSegmentMatches.OnTrail(source.Classified.Profile, unrelated);
        terrainProfiles.Seed(third.SessionId, source.Key, unrelated);

        var average = Assert.IsType<SegmentTrailAverage.Averaged>(await CreateCoordinator().BuildTrailAverageAsync(
            source, new(0, source.Classified.Profile.Windows[^1].EndSeconds), [first, second, third], TestContext.Current.CancellationToken));
        Assert.Equal(3, average.Measurement.RunCount);
        Assert.Contains(average.Roughness, s => s.Role == SegmentComparisonSeriesRole.FrameImu && s.Runs is { Count: > 0 });
        Assert.Contains(average.Grade, s => s.Role == SegmentComparisonSeriesRole.Grade && s.Runs is { Count: > 0 });
    }

    [Fact]
    public async Task MissingMatchProfileCannotSupplyThirdRun()
    {
        var (source, first) = TerrainComparisonInputs(0);
        var absent = TestSegmentMatches.OnTrail(source.Classified.Profile, earlierRide.Value);
        var result = await CreateCoordinator().BuildTrailAverageAsync(source, new(0, source.Classified.Profile.Windows[^1].EndSeconds),
            [first, absent], TestContext.Current.CancellationToken);
        Assert.Equal(2, Assert.IsType<SegmentTrailAverage.TooFewRuns>(result).RunCount);
    }

    [Fact]
    public async Task AveragingFailureReturnsFailed()
    {
        var (source, first) = TerrainComparisonInputs(0);
        var second = TestSegmentMatches.OnTrail(source.Classified.Profile, earlierRide.Value);
        terrainProfiles.ReadFailure = new IOException("profile read failed");
        var result = await CreateCoordinator().BuildTrailAverageAsync(source, new(0, source.Classified.Profile.Windows[^1].EndSeconds),
            [first, second], TestContext.Current.CancellationToken);
        Assert.IsType<SegmentTrailAverage.Failed>(result);
    }

    [Fact]
    public async Task OneMatchGivesTooFewRunsWithoutReadingProfiles()
    {
        var (source, match) = TerrainComparisonInputs(0);
        var reads = 0;
        terrainProfiles.BeforeRead = (_, _) =>
        {
            reads++;
            return Task.CompletedTask;
        };
        var result = await CreateCoordinator().BuildTrailAverageAsync(source, new(0, source.Classified.Profile.Windows[^1].EndSeconds),
            [match], TestContext.Current.CancellationToken);
        Assert.Equal(2, Assert.IsType<SegmentTrailAverage.TooFewRuns>(result).RunCount);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task CancellingSessionOperationDuringProfileReadCancelsTheAverage()
    {
        var (source, match) = TerrainComparisonInputs(0);
        var repeated = TestSegmentMatches.OnTrail(source.Classified.Profile, earlierRide.Value);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        terrainProfiles.BeforeRead = (_, token) =>
        {
            reading.SetResult();
            return Task.Delay(Timeout.Infinite, token);
        };
        var work = CreateCoordinator().BuildTrailAverageAsync(source, new(0, 10), [match, repeated], TestContext.Current.CancellationToken);
        await reading.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        operationRunner.Cancel(source.SessionId);
        Assert.IsType<SegmentTrailAverage.Cancelled>(await work.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
    }

    private (SegmentTerrainSource Source, SegmentMatch Match) TerrainComparisonInputs(int baselineCount,
        TerrainProfile? current = null, TerrainProfile? earlier = null)
    {
        current ??= currentRide.Value;
        earlier ??= earlierRide.Value;
        var match = TestSegmentMatches.OnTrail(current, earlier);
        var key = TerrainProfileKey.From(match.ContentToken);
        terrainProfiles.Seed(match.SessionId, key, earlier);
        var references = Enumerable.Range(0, baselineCount).Select(i =>
        {
            var id = Guid.NewGuid();
            terrainProfiles.Seed(id, key, SyntheticResponseRides.Baseline[i]);
            return new SessionProfileReference(id, key);
        }).ToImmutableArray();
        return (new(sourceId, key, TerrainClassifier.Classify(current, SyntheticRide.Scale), new(references), TerrainScaleCatalog.Empty), match);
    }

    private static SegmentTerrainSource IncludeMatch(SegmentTerrainSource source, SegmentMatch match) =>
        source with { Baseline = new(source.Baseline.Profiles.Add(new(match.SessionId, TerrainProfileKey.From(match.ContentToken)))) };

    private Task<SegmentTerrainComparison> CompareTerrain(SegmentTerrainSource? source, SegmentMatch match,
        TrailAverage.Averaged? average = null) =>
        CreateCoordinator().BuildTerrainComparisonAsync(match, source, average, TestContext.Current.CancellationToken);
}
