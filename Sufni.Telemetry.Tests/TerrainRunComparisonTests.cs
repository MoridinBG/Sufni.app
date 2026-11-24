namespace Sufni.Telemetry.Tests;

public class TerrainRunComparisonTests
{
    private static readonly Lazy<TerrainProfile> earlier = new(() => SyntheticTrailRuns.Record(42));

    [Fact]
    public void RepeatedResponseLawStaysInsideRepeatRanges()
    {
        var rows = Compare(SyntheticTrailRuns.Record(42, recordingSeed: 45));
        foreach (var row in rows)
        {
            // The ground has calm and rough blocks only; rough windows reach the medium class only
            // at the low end of their spread, which gives under 300 m.
            var expected = row.Class == RoughnessClass.Medium ? ResponseCellState.TooShort : ResponseCellState.WithinRepeatRange;
            Assert.All(row.Responses.Values, cell => Assert.Equal(expected, cell.State));
            Assert.Equal(expected, row.FrontSamePosition.State);
            Assert.Equal(expected, row.RearSamePosition.State);
        }
    }

    [Fact]
    public void ReducedRoughReboundAppearsOnlyInRoughClass()
    {
        var rows = Compare(SyntheticTrailRuns.Record(42, roughRebound: 0.7));
        foreach (var side in new[] { ResponseTarget.FrontPeakRebound, ResponseTarget.RearPeakRebound })
        {
            var rough = rows.Single(row => row.Class == RoughnessClass.Rough).Responses[side];
            Assert.Equal(ResponseCellState.Finding, rough.State);
            Assert.InRange(rough.Ratio!.Value, 0.65, 0.75);
            Assert.DoesNotContain(rows.Where(row => row.Class != RoughnessClass.Rough), row => row.Responses[side].State == ResponseCellState.Finding);
        }
    }

    [Fact]
    public void OnlyCalmSectionsLosePace()
    {
        var rows = Compare(SyntheticTrailRuns.Record(42, calmSpeedFactor: 0.8));
        Assert.InRange(rows.Single(row => row.Class == RoughnessClass.Calm).Pace!.Value.Median, 0.77, 0.83);
        Assert.InRange(rows.Single(row => row.Class == RoughnessClass.Rough).Pace!.Value.Median, 0.97, 1.03);
    }

    [Fact]
    public void TwoHundredMatchedMetresAreTooShort()
    {
        var profile = earlier.Value;
        var rows = TerrainRunComparison.Compare(profile, profile, TerrainProfiles.Identity(20), SuspensionResponseModel.FitPair(profile, profile, []));
        var all = rows.Single(row => row.Class is null);
        Assert.Equal(200, all.DistanceMetres);
        Assert.Null(all.Pace);
        Assert.Equal(ResponseCellState.TooShort, all.FrontSamePosition.State);
        Assert.All(all.Responses.Values, cell => Assert.Equal(ResponseCellState.TooShort, cell.State));
    }

    [Fact]
    public void AveragedClassesDetermineMatchedDistanceIncludingUnclassifiedGaps()
    {
        var profile = earlier.Value;
        var alignment = TerrainProfiles.Identity(60);
        var windows = alignment.Pairs.Take(50).Select((pair, i) =>
        {
            var w = profile.Windows[pair.SourceIndex];
            return new TrailAverageWindow(pair.SourceIndex, w.StartSeconds, w.EndSeconds, 1.7, null, null,
                i < 40 ? RoughnessClass.Medium : null, null);
        });
        var rows = TerrainRunComparison.Compare(profile, profile, alignment, SuspensionResponseModel.FitPair(profile, profile, []),
            new TrailAverage.Averaged(4, [.. windows]));
        Assert.Equal(400, rows.Single(r => r.Class == RoughnessClass.Medium).DistanceMetres);
        Assert.Equal(0, rows.Single(r => r.Class == RoughnessClass.Rough).DistanceMetres);
        Assert.Equal(600, rows.Single(r => r.Class is null).DistanceMetres);
    }

    private static IReadOnlyList<TerrainComparisonRow> Compare(TerrainProfile current)
    {
        var previous = earlier.Value;
        var alignment = Assert.IsType<TerrainRunAlignment.Aligned>(TerrainRunAlignment.Calculate(current, previous, SyntheticTrailRuns.Map(current, previous)));
        var model = SuspensionResponseModel.FitPair(current, previous, SyntheticResponseRides.Baseline);
        return TerrainRunComparison.Compare(current, previous, alignment, model);
    }
}
