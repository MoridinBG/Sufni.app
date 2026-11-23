namespace Sufni.Telemetry.Tests;

public class TerrainRunComparisonTests
{
    private static readonly Lazy<TerrainProfile> earlier = new(() => SyntheticTrailRuns.Record(42));

    [Fact]
    public void RepeatedResponseLawStaysInsideRepeatRanges()
    {
        var rows = Compare(SyntheticTrailRuns.Record(42, recordingSeed: 45));
        foreach (var row in rows.Where(row => row.Class is null or RoughnessClass.Calm or RoughnessClass.Rough))
        {
            Assert.All(row.Responses.Values, cell => Assert.Equal(ResponseCellState.WithinRepeatRange, cell.State));
            Assert.Equal(ResponseCellState.WithinRepeatRange, row.FrontSamePosition.State);
            Assert.Equal(ResponseCellState.WithinRepeatRange, row.RearSamePosition.State);
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
        var alignment = new TerrainRunAlignment.Aligned(ImuLocation.Frame, [],
            [.. Enumerable.Range(0, 20).Select(i => new TerrainWindowPair(i, i))]);
        var rows = TerrainRunComparison.Compare(profile, profile, alignment, SuspensionResponseModel.FitPair(profile, profile, []));
        var all = rows.Single(row => row.Class is null);
        Assert.Equal(200, all.DistanceMetres);
        Assert.Null(all.Pace);
        Assert.Equal(ResponseCellState.TooShort, all.FrontSamePosition.State);
        Assert.All(all.Responses.Values, cell => Assert.Equal(ResponseCellState.TooShort, cell.State));
    }

    private static IReadOnlyList<TerrainComparisonRow> Compare(TerrainProfile current)
    {
        var previous = earlier.Value;
        var alignment = Assert.IsType<TerrainRunAlignment.Aligned>(TerrainRunAlignment.Calculate(current, previous, SyntheticTrailRuns.Map(current, previous)));
        var model = SuspensionResponseModel.FitPair(current, previous, SyntheticResponseRides.Baseline);
        return TerrainRunComparison.Compare(current, previous, alignment, model);
    }
}
