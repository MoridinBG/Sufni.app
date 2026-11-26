using Sufni.App.Segments.ViewModels;
using Sufni.App.Sessions.Models;

namespace Sufni.App.Tests.Segments.ViewModels;

public class SegmentTuneReportTests
{
    [Fact]
    public void EachChangeGivesARowWithBothValues()
    {
        var report = SegmentTuneReport.From(new TuneComparison(
            [new TuneChange(TuneField.FrontSpringRate, "80 psi", "75 psi"), new(TuneField.RearLowSpeedRebound, "8", "10")], 3, 5));

        Assert.Equal([("80 psi", "75 psi"), ("8", "10")], report.Rows.Select(row => (row.Current, row.Matched)));
        Assert.True(report.HasRows);
    }

    [Fact]
    public void ComparisonWithoutValuesOnBothRidesHasNoRow()
    {
        var report = SegmentTuneReport.From(new TuneComparison([], 0, 10));

        Assert.False(report.HasRows);
    }
}
