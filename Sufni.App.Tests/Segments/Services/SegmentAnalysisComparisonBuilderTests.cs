using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Segments.Services;

public class SegmentAnalysisComparisonBuilderTests
{
    [Fact]
    public void Build_AddsDistributionOverlaysForTheMatchedSidesOnly()
    {
        var analysis = Build(SessionDampingPercentages.Empty);

        Assert.Equal(
            [
                (SegmentAnalysisPlot.TravelDistribution, SuspensionType.Front),
                (SegmentAnalysisPlot.VelocityDistribution, SuspensionType.Front),
            ],
            analysis.Overlays.Select(overlay => (overlay.Plot, overlay.Side)));
    }

    [Fact]
    public void Build_DampingMetricDelta_IsTheCurrentValueMinusTheMatchValue()
    {
        var current = SessionDampingPercentages.FromSides(
            new SessionDampingSidePercentages(HscPercentage: 40, LscPercentage: null, LsrPercentage: null, HsrPercentage: null),
            SessionDampingSidePercentages.Empty);

        var analysis = Build(current);

        var hsc = Assert.Single(analysis.Metrics, metric => metric.Band == DampingBand.Hsc);
        Assert.Equal(SuspensionType.Front, hsc.Side);
        Assert.Equal(100, hsc.MatchValue, precision: 6);
        Assert.Equal(-60, hsc.Delta!.Value, precision: 6);
        Assert.Null(Assert.Single(analysis.Metrics, metric => metric.Band == DampingBand.Lsc).Delta);
    }

    private static SegmentAnalysisComparison Build(SessionDampingPercentages current)
    {
        var telemetry = TestSegmentMatches.Telemetry();
        var comparison = SegmentComparisonBuilder.Build(TestSegmentMatches.Match(20, 22), telemetry, sourceDurationSeconds: 30)!;
        return SegmentAnalysisComparisonBuilder.Build(
            comparison,
            SegmentComparisonBuilder.BuildDetail(telemetry, matchedTrackPoints: null),
            TravelDistributionMode.ActiveSuspension,
            VelocityAverageMode.SampleAveraged,
            DampingSpeedCutoffs.Default,
            current);
    }
}
