namespace Sufni.Telemetry.Tests;

public class TerrainRunComparisonTests
{
    private static readonly Lazy<TerrainProfile> earlier = new(() => SyntheticTrailRuns.Record(42));

    [Fact]
    public void RepeatedResponseLawStaysInsideRepeatRanges()
    {
        var rows = Compare(SyntheticTrailRuns.Record(42, recordingSeed: 45));
        var described = SyntheticTrailRuns.Describe(42);
        foreach (var row in rows)
        {
            var metres = row.Class is { } roughness
                ? described.Count(section => section.Roughness == roughness) * SyntheticTrailRuns.SectionMetres
                : SyntheticTrailRuns.TrailMetres;
            var expected = metres >= SuspensionResponseModel.MinimumReportMetres
                ? ResponseCellState.WithinRepeatRange : ResponseCellState.TooShort;
            Assert.All(row.Responses.Values, cell => Assert.Equal(expected, cell.State));
            Assert.Equal(expected, row.FrontSamePosition.State);
            Assert.Equal(expected, row.RearSamePosition.State);
        }
    }

    [Fact]
    public void ReducedRoughReboundAppearsOnlyInRoughLevels()
    {
        var rows = Compare(SyntheticTrailRuns.Record(42, roughRebound: 0.7));
        foreach (var side in new[] { ResponseTarget.FrontPeakRebound, ResponseTarget.RearPeakRebound })
        {
            Assert.All(rows.Where(row => row.Class >= RoughnessClass.Rough), row =>
            {
                Assert.Equal(ResponseCellState.Finding, row.Responses[side].State);
                Assert.InRange(row.Responses[side].Ratio!.Value, 0.65, 0.75);
            });
            Assert.DoesNotContain(rows.Where(row => row.Class < RoughnessClass.Rough), row => row.Responses[side].State == ResponseCellState.Finding);
        }
        var grouped = TerrainRunComparison.Findings(rows).Where(f => f.Group is not null).ToArray();
        Assert.NotEmpty(grouped);
        Assert.All(grouped, finding =>
        {
            Assert.True(finding.Group >= RoughnessClass.Rough);
            Assert.InRange(finding.Deviation, -0.35, -0.25);
        });
    }

    [Fact]
    public void OnlySmoothAndCalmSectionsLosePace()
    {
        var rows = Compare(SyntheticTrailRuns.Record(42, calmSpeedFactor: 0.8));
        Assert.All(rows.Where(row => row.Class <= RoughnessClass.Calm), row => Assert.InRange(row.Pace!.Value.Median, 0.77, 0.83));
        Assert.All(rows.Where(row => row.Class >= RoughnessClass.Rough), row => Assert.InRange(row.Pace!.Value.Median, 0.97, 1.03));
    }

    [Fact]
    public void HundredFortyMatchedMetresAreTooShort()
    {
        var all = CompareWithItself(14);
        Assert.Null(all.Pace);
        Assert.Equal(ResponseCellState.TooShort, all.FrontSamePosition.State);
        Assert.All(all.Responses.Values, cell => Assert.Equal(ResponseCellState.TooShort, cell.State));
    }

    [Fact]
    public void TwoHundredMatchedMetresAreReported()
    {
        var all = CompareWithItself(20);
        Assert.Equal(1, all.Pace!.Value.Median, 8);
        Assert.Equal(new ComparisonRatio(ResponseCellState.WithinRepeatRange, 1), all.FrontSamePosition);
    }

    [Fact]
    public void AveragedClassesDetermineMatchedDistanceIncludingUnclassifiedGaps()
    {
        var profile = earlier.Value;
        var alignment = TerrainProfiles.Identity(60);
        var windows = alignment.Pairs.Take(50).Select((pair, i) =>
        {
            var w = profile.Windows[pair.SourceIndex];
            return new TrailAverageWindow(pair.SourceIndex, w.StartSeconds, w.EndSeconds, 1.7, null, null, null,
                i < 40 ? RoughnessClass.Medium : null, null);
        });
        var rows = TerrainRunComparison.Compare(profile, profile, alignment, SuspensionResponseModel.FitPair(profile, profile, []),
            new TrailAverage.Averaged(4, [.. windows]));
        Assert.Equal(400, rows.Single(r => r.Class == RoughnessClass.Medium).DistanceMetres);
        Assert.Equal(0, rows.Single(r => r.Class == RoughnessClass.Rough).DistanceMetres);
        Assert.Equal(600, rows.Single(r => r.Class is null).DistanceMetres);
    }

    private static TerrainComparisonRow CompareWithItself(int windows)
    {
        var profile = earlier.Value;
        return TerrainRunComparison.Compare(profile, profile, TerrainProfiles.Identity(windows),
            SuspensionResponseModel.FitPair(profile, profile, [])).Single(row => row.Class is null);
    }

    private static IReadOnlyList<TerrainComparisonRow> Compare(TerrainProfile current)
    {
        var previous = earlier.Value;
        var alignment = Assert.IsType<TerrainRunAlignment.Aligned>(TerrainRunAlignment.Calculate(current, previous, SyntheticTrailRuns.Map(current, previous)));
        var model = SuspensionResponseModel.FitPair(current, previous, SyntheticResponseRides.Baseline);
        return TerrainRunComparison.Compare(current, previous, alignment, model);
    }
}
