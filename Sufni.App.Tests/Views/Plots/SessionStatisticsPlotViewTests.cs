using System;
using Avalonia.Headless.XUnit;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.TestTelemetryData;

namespace Sufni.App.Tests.Views.Plots;

public class SessionStatisticsPlotViewTests
{
    [AvaloniaFact]
    public async Task SessionStatisticsPlotView_UsesTravelHistogramPlot_ForTravelHistogramKind()
    {
        var view = new TestableSessionStatisticsPlotView
        {
            PlotKind = PlotKind.TravelHistogram,
            SuspensionType = SuspensionType.Front,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        Assert.Equal(typeof(TravelHistogramPlot), mounted.View.PlotModelType);
    }

    [AvaloniaFact]
    public async Task SessionStatisticsPlotView_UsesTravelFrequencyHistogramPlot_ForTravelFrequencyKind()
    {
        var view = new TestableSessionStatisticsPlotView
        {
            PlotKind = PlotKind.TravelFrequencyHistogram,
            SuspensionType = SuspensionType.Rear,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        Assert.Equal(typeof(TravelFrequencyHistogramPlot), mounted.View.PlotModelType);
    }

    [AvaloniaFact]
    public async Task SessionStatisticsPlotView_UsesVelocityHistogramPlot_ForVelocityHistogramKind()
    {
        var view = new TestableSessionStatisticsPlotView
        {
            PlotKind = PlotKind.VelocityHistogram,
            SuspensionType = SuspensionType.Front,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        Assert.Equal(typeof(VelocityHistogramPlot), mounted.View.PlotModelType);
    }

    [AvaloniaFact]
    public async Task SessionStatisticsPlotView_UsesBalancePlot_ForBalanceKind()
    {
        var view = new TestableSessionStatisticsPlotView
        {
            PlotKind = PlotKind.Balance,
            BalanceType = BalanceType.Compression,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        Assert.Equal(typeof(BalancePlot), mounted.View.PlotModelType);
    }

    [AvaloniaFact]
    public async Task SessionStatisticsPlotView_AnalysisRangeChangeReloadsStatisticsPlot()
    {
        var view = new TestableSessionStatisticsPlotView
        {
            PlotKind = PlotKind.TravelHistogram,
            SuspensionType = SuspensionType.Front,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Null(mounted.View.PlotAnalysisRange);

        var range = new TelemetryTimeRange(0.25, 0.75);
        view.AnalysisRange = range;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(range, mounted.View.PlotAnalysisRange);
    }

    private sealed class TestableSessionStatisticsPlotView : SessionStatisticsPlotView
    {
        public Type PlotModelType => PlotModel.GetType();
        public TelemetryTimeRange? PlotAnalysisRange => PlotModel.AnalysisRange;
    }
}
