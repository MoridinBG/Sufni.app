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
    public void AgainstUsual_KeepsTransmissionOutOfVerdictsAndChart_ButInDetails()
    {
        var report = TerrainResponseReports.AgainstUsual([Row(null, 900, Finding(ResponseTarget.Transmission, 1.3))]);

        Assert.Empty(report.Fork.Concat(report.Shock));
        Assert.DoesNotContain(report.Chart.Single().Fork.Concat(report.Chart.Single().Shock), bar => bar.Target == ResponseTarget.Transmission);
        Assert.Equal(Enum.GetValues<ResponseTarget>().Length, report.Details.Single().Values.Length);
        Assert.NotNull(report.Summary);
    }

    [Fact]
    public void AgainstUsual_LeavesShortGroupsOut_AndMarksLowConfidence()
    {
        var report = TerrainResponseReports.AgainstUsual([
            TooShort(RoughnessClass.Calm), Row(RoughnessClass.Rough, 200), Row(null, 900)
        ]);

        Assert.Equal([(null, false), (RoughnessClass.Rough, true)],
            report.Chart.Select(row => (row.Group, row.IsLowConfidence)));
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
}
