using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Segments.ViewModels;

public class SegmentComparisonRowViewModelTests
{
    private readonly SegmentComparison comparison = SegmentComparisonBuilder.Build(
        TestSegmentMatches.Match(19, 23),
        TestSegmentMatches.Telemetry(),
        sourceDurationSeconds: 30)!;

    [Fact]
    public void TravelRow_TogglesTheAirtimeOfTheMatch()
    {
        var row = CreateRow(SegmentComparisonSignal.Travel);
        var airtime = row.HeaderActions.Single(action => action.Id == "airtime");
        Assert.True(row.ShowAirtime);

        airtime.Command!.Execute(null);

        Assert.False(row.ShowAirtime);
        Assert.False(airtime.IsChecked);
    }

    [Fact]
    public void VelocityRow_HasNoAirtimeToggle()
    {
        var row = CreateRow(SegmentComparisonSignal.Velocity);

        Assert.Equal(["compare-analysis", "remove"], row.HeaderActions.Select(action => action.Id));
        Assert.False(row.ShowAirtime);
    }

    private SegmentComparisonRowViewModel CreateRow(SegmentComparisonSignal signal) =>
        new(comparison, signal, _ => Task.CompletedTask, _ => { });
}
