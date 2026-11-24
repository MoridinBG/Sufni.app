using Sufni.App.Segments.Models;
using Sufni.App.Segments.ViewModels;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.Fixtures.TestResponseRows;

namespace Sufni.App.Tests.Segments.ViewModels;

public class SegmentTerrainReportTests
{
    [Fact]
    public void ComparedRunsLeadWithThePaceOfAllMatchedGround()
    {
        var report = SegmentTerrainReport.From(new SegmentTerrainComparison.Compared([
            Compared(RoughnessClass.Rough, 300, new(0.9, 1, 1.1)), Compared(null, 600, new(0.9, 1.06, 1.12))
        ]));

        Assert.True(report.HasPace);
        Assert.True(report.HasResponse);
    }

    [Fact]
    public void ShortMatchedGroundHasNoPace()
    {
        var report = SegmentTerrainReport.From(new SegmentTerrainComparison.Compared([Compared(null, 100, null)]));

        Assert.False(report.HasPace);
    }

    [Fact]
    public void UnavailableComparisonExplainsItselfWithoutAResponse()
    {
        var report = SegmentTerrainReport.From(new SegmentTerrainComparison.Unavailable(SegmentTerrainUnavailable.Unaligned));

        Assert.True(report.HasMessage);
        Assert.False(report.HasResponse);
    }
}
