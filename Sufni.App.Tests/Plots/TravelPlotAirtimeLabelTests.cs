using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Plots;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.TestTelemetryData;
using static Sufni.App.Tests.Infrastructure.PlotTestHelpers;

namespace Sufni.App.Tests.Plots;

public class TravelPlotAirtimeLabelTests
{
    [Fact]
    public void UpdateAirtimeLabelVisibility_WhenLabelsFit_KeepsAllLabelsVisible()
    {
        var plot = new Plot();
        var sut = new TravelPlot(plot);
        var telemetry = CreateTelemetryWithAirtimes(
        [
            new Airtime { Start = 1.8, End = 2.2 },
            new Airtime { Start = 7.8, End = 8.2 },
        ]);

        sut.LoadTelemetryData(telemetry);
        sut.UpdateAirtimeLabelVisibility(visibleMinimumSeconds: 0, visibleMaximumSeconds: 10, dataAreaWidthPixels: 500);

        Assert.Equal([true, true], GetAirtimeLabels(plot).Select(label => label.IsVisible).ToArray());
    }

    [Fact]
    public void UpdateAirtimeLabelVisibility_WhenLabelsCollide_KeepsLongerAirtimeLabel()
    {
        var plot = new Plot();
        var sut = new TravelPlot(plot);
        var telemetry = CreateTelemetryWithAirtimes(
        [
            new Airtime { Start = 1.90, End = 2.10 },
            new Airtime { Start = 1.75, End = 2.25 },
            new Airtime { Start = 8.00, End = 8.30 },
        ]);

        sut.LoadTelemetryData(telemetry);
        sut.UpdateAirtimeLabelVisibility(visibleMinimumSeconds: 0, visibleMaximumSeconds: 10, dataAreaWidthPixels: 500);

        Assert.Equal([false, true, true], GetAirtimeLabels(plot).Select(label => label.IsVisible).ToArray());
    }

    [Fact]
    public void UpdateAirtimeLabelVisibility_WhenLabelsCollide_KeepsSpansVisible()
    {
        var plot = new Plot();
        var sut = new TravelPlot(plot);
        var telemetry = CreateTelemetryWithAirtimes(
        [
            new Airtime { Start = 1.90, End = 2.10 },
            new Airtime { Start = 1.75, End = 2.25 },
        ]);

        sut.LoadTelemetryData(telemetry);
        sut.UpdateAirtimeLabelVisibility(visibleMinimumSeconds: 0, visibleMaximumSeconds: 10, dataAreaWidthPixels: 500);

        Assert.Equal([false, true], GetAirtimeLabels(plot).Select(label => label.IsVisible).ToArray());
        Assert.All(plot.PlottableList.OfType<HorizontalSpan>(), span => Assert.True(span.IsVisible));
    }

    private static TelemetryData CreateTelemetryWithAirtimes(Airtime[] airtimes)
    {
        var telemetry = CreateMinimal(duration: 10, sampleRate: 2);
        telemetry.Airtimes = airtimes;
        return telemetry;
    }

}
