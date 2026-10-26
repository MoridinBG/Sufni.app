using Sufni.App.Segments.Models;
using Sufni.App.Segments.ViewModels;

namespace Sufni.App.Tests.TestSupport.Fixtures;

/// <summary>
/// Comparison rows over hand-made series, for plot and view tests.
/// </summary>
internal static class TestSegmentComparisonRows
{
    public static SegmentComparisonRowViewModel Create(
        SegmentComparisonSignal signal,
        IReadOnlyList<SegmentComparisonSeries> series,
        IReadOnlyList<SegmentMatchedAirtime>? airtimes = null)
    {
        var comparison = new SegmentComparison(
            TestSegmentMatches.Match(0, 10),
            sourceDurationSeconds: 100,
            travelSeries: signal == SegmentComparisonSignal.Travel ? series : [],
            velocitySeries: signal == SegmentComparisonSignal.Velocity ? series : [],
            imuSeries: signal == SegmentComparisonSignal.Imu ? series : [],
            speedSeries: signal == SegmentComparisonSignal.Speed ? series : [],
            airtimes: airtimes ?? [],
            matchedSegmentTrackPoints: [],
            analysisRange: null);
        return new SegmentComparisonRowViewModel(comparison, signal, _ => { }, _ => { });
    }

    /// <summary>A series with one value per second from zero.</summary>
    public static SegmentComparisonSeries Series(SegmentComparisonSeriesRole role, params double[] values) =>
        new(role.ToString(), "unit", role, values.Select((_, index) => (double)index).ToArray(), values);
}
