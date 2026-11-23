using System.Collections.Immutable;
using Sufni.App.Bikes.Models;
using NSubstitute;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Terrain.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Segments.Coordinators;

public partial class SegmentCoordinatorTests
{
    private static readonly Lazy<TerrainProfile> earlierRide = new(() => SyntheticTrailRuns.Record(8, roughRebound: 0.60));
    private static readonly Lazy<TerrainProfile> currentRide = new(() => SyntheticTrailRuns.Record(8, calmSpeedFactor: 0.8));
    private readonly Dictionary<Guid, (TerrainProfileKey Key, TerrainProfile Profile)> cachedProfiles = [];

    [Fact]
    public async Task MatchInsideBaselineDoesNotInfluenceQualifiedPairRatios()
    {
        var (source, match) = TerrainComparisonInputs(5);
        var excluding = await CompareTerrain(source, match);
        var including = await CompareTerrain(IncludeMatch(source, match), match);
        var expected = Assert.IsType<SegmentTerrainComparison.Compared>(excluding.Terrain).Rows.Single(r => r.Class is null);
        var actual = Assert.IsType<SegmentTerrainComparison.Compared>(including.Terrain).Rows.Single(r => r.Class is null);
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
        var all = Assert.IsType<SegmentTerrainComparison.Compared>(result.Terrain).Rows.Single(r => r.Class is null);
        Assert.All(all.Responses.Values, cell => Assert.Equal(ResponseCellState.NoBaseline, cell.State));
        Assert.NotNull(all.Pace);
        Assert.NotNull(all.FrontSamePosition.Ratio);
        Assert.NotNull(all.RearSamePosition.Ratio);
    }

    [Fact]
    public async Task MatchWithOlderProfileKeepsOrdinaryAnalysis()
    {
        var (source, match) = TerrainComparisonInputs(0);
        var (key, profile) = cachedProfiles[match.SessionId];
        cachedProfiles[match.SessionId] = (key with { AlgorithmVersion = key.AlgorithmVersion - 1 }, profile);
        var result = await CompareTerrain(source, match);
        Assert.Equal(SegmentTerrainUnavailable.NoMatchProfile, Assert.IsType<SegmentTerrainComparison.Unavailable>(result.Terrain).Reason);
        Assert.NotEmpty(result.Metrics);
    }

    [Fact]
    public async Task ProfileReadFailureKeepsOrdinaryAnalysisAndReportsTerrainError()
    {
        var (source, match) = TerrainComparisonInputs(0);
        terrainProfiles.ReadAsync(Arg.Any<IReadOnlyDictionary<Guid, TerrainProfileKey>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyDictionary<Guid, TerrainProfile>>(new IOException("profile read failed")));
        var result = await CompareTerrain(source, match);
        Assert.IsType<SegmentTerrainComparison.Failed>(result.Terrain);
        Assert.NotEmpty(result.Overlays);
        Assert.NotEmpty(result.Metrics);
    }

    private (SegmentTerrainSource Source, SegmentMatch Match) TerrainComparisonInputs(int baselineCount)
    {
        var current = currentRide.Value;
        var earlier = earlierRide.Value;
        var match = TestSegmentMatches.OnTrail(current, earlier);
        var key = TerrainProfileKey.From(match.ContentToken);
        cachedProfiles[match.SessionId] = (key, earlier);
        var references = Enumerable.Range(0, baselineCount).Select(i =>
        {
            var id = Guid.NewGuid();
            cachedProfiles[id] = (key, SyntheticResponseRides.Baseline[i]);
            return new SessionProfileReference(id, key);
        }).ToImmutableArray();
        terrainProfiles.ReadAsync(Arg.Any<IReadOnlyDictionary<Guid, TerrainProfileKey>>(), Arg.Any<CancellationToken>())
            .Returns(call => cachedProfiles.Where(p => call.Arg<IReadOnlyDictionary<Guid, TerrainProfileKey>>().TryGetValue(p.Key, out var k) && p.Value.Key == k)
                .ToDictionary(p => p.Key, p => p.Value.Profile));
        return (new(sourceId, key, current, new(references)), match);
    }

    private static SegmentTerrainSource IncludeMatch(SegmentTerrainSource source, SegmentMatch match) =>
        source with { Baseline = new(source.Baseline.Profiles.Add(new(match.SessionId, TerrainProfileKey.From(match.ContentToken)))) };

    private async Task<SegmentAnalysisComparison> CompareTerrain(SegmentTerrainSource? source, SegmentMatch match)
    {
        var telemetry = TestSegmentMatches.Telemetry();
        var comparison = SegmentComparisonBuilder.Build(match, telemetry, telemetry.Metadata.Duration)!;
        var result = await CreateCoordinator().BuildAnalysisComparisonAsync(comparison,
            SegmentComparisonBuilder.BuildDetail(telemetry, null), TravelDistributionMode.ActiveSuspension,
            VelocityAverageMode.SampleAveraged, DampingSpeedCutoffs.Default, SessionDampingPercentages.Empty, source,
            TestContext.Current.CancellationToken);
        return Assert.IsType<SegmentAnalysisComparisonResult.Built>(result).Comparison;
    }
}
