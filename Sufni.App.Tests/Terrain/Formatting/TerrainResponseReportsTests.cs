using Sufni.App.Terrain.Formatting;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.Fixtures.TestResponseRows;

namespace Sufni.App.Tests.Terrain.Formatting;

public class TerrainResponseReportsTests
{
    [Fact]
    public void AgainstUsual_SplitsVerdictsByEnd_AllGroundFirst()
    {
        var report = TerrainResponseReports.AgainstUsual([
            Row(RoughnessClass.Rough, 400, Finding(ResponseTarget.FrontRms, 0.6), Finding(ResponseTarget.RearPeakRebound, 1.5)),
            Row(null, 900, Finding(ResponseTarget.FrontPeakCompression, 1.4))
        ]);

        Assert.Equal([(ResponseTarget.FrontPeakCompression, null), (ResponseTarget.FrontRms, RoughnessClass.Rough)],
            report.Fork.Select(v => (v.Finding.Target, v.Finding.Group)));
        Assert.Equal(ResponseTarget.RearPeakRebound, Assert.Single(report.Shock).Finding.Target);
        Assert.Null(report.Summary);
    }

    [Fact]
    public void AgainstUsual_LeavesTransmissionOutOfTheReport()
    {
        var report = TerrainResponseReports.AgainstUsual([Row(null, 900, Finding(ResponseTarget.Transmission, 1.3))]);

        Assert.Empty(report.Fork.Concat(report.Shock));
        var bars = report.Chart.Single().Fork.Concat(report.Chart.Single().Shock).ToArray();
        Assert.DoesNotContain(bars, bar => bar.Target == ResponseTarget.Transmission);
        Assert.NotNull(report.Summary);
    }

    [Fact]
    public void AgainstUsual_LeavesShortGroupsOut_AndMarksLowConfidence()
    {
        var report = TerrainResponseReports.AgainstUsual([
            TooShort(RoughnessClass.Calm), Row(RoughnessClass.Rough, 200), Row(null, 900)
        ]);

        Assert.Equal([(null, false), (RoughnessClass.Rough, true)], report.Chart.Select(row => (row.Group, row.IsLowConfidence)));
    }

    [Fact]
    public void AgainstUsual_MarksVerdictsFromShortGroundAsLowConfidence()
    {
        var report = TerrainResponseReports.AgainstUsual([
            Row(RoughnessClass.Rough, 200, Finding(ResponseTarget.FrontRms, 0.6)), Row(null, 900, Finding(ResponseTarget.FrontRms, 0.6))
        ]);

        Assert.Equal([(null, false), (RoughnessClass.Rough, true)],
            report.Fork.Select(verdict => (verdict.Finding.Group, verdict.IsLowConfidence)));
    }

    [Theory]
    [InlineData(1.1, 0.5)]
    [InlineData(1.73, 0.8)]
    [InlineData(0.3, 0.7)]
    public void AgainstUsual_SharesOneScaleFromTheLargestDeviation(double ratio, double scale)
    {
        var report = TerrainResponseReports.AgainstUsual([
            Row(RoughnessClass.Rough, 400, Cell(ResponseTarget.RearRms, ratio)), Row(null, 900)
        ]);

        Assert.Equal(scale, report.Scale, 8);
    }

    [Fact]
    public void AgainstUsual_HasNoChart_WhenEveryGroupIsTooShort()
    {
        var report = TerrainResponseReports.AgainstUsual([TooShort(null), TooShort(RoughnessClass.Rough)]);

        Assert.Empty(report.Chart);
        Assert.NotNull(report.Summary);
    }

    [Fact]
    public void AgainstMatchedRun_JudgesEachTargetByTheTrailRepeatRange()
    {
        var report = TerrainResponseReports.AgainstMatchedRun([Compared(null, 600, new(0.9, 1, 1.1))]);

        var row = Assert.Single(report.Chart);
        Assert.Equal(0.20, row.Fork.Single(bar => bar.Target == ResponseTarget.FrontRms).RepeatRange);
        Assert.Equal(0.25, row.Shock.Single(bar => bar.Target == ResponseTarget.RearPeakRebound).RepeatRange);
    }

    [Fact]
    public void AgainstMatchedRun_AddsSamePositionActivityToEachEnd()
    {
        var row = Compared(null, 600, new(0.9, 1, 1.1)) with
        {
            FrontSamePosition = new(ResponseCellState.Finding, 1.4), RearSamePosition = new(ResponseCellState.WithinRepeatRange, 1.05)
        };

        var chart = Assert.Single(TerrainResponseReports.AgainstMatchedRun([row]).Chart);

        var front = Assert.Single(chart.Fork, bar => bar.Target is null);
        var rear = Assert.Single(chart.Shock, bar => bar.Target is null);
        Assert.Equal((true, 0.4), (front.IsFinding, Math.Round(front.Deviation!.Value, 8)));
        Assert.Equal((false, 0.05), (rear.IsFinding, Math.Round(rear.Deviation!.Value, 8)));
        Assert.Equal(0.20, front.RepeatRange);
    }

    [Fact]
    public void AgainstMatchedRun_ReportsFindingsAsVerdicts()
    {
        var report = TerrainResponseReports.AgainstMatchedRun([
            Compared(null, 600, new(0.9, 1, 1.1), (ResponseTarget.RearRms, new(ResponseCellState.Finding, 1.3)))
        ]);

        Assert.Equal(ResponseTarget.RearRms, Assert.Single(report.Shock).Finding.Target);
        Assert.Empty(report.Fork);
    }
}
