using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Views.Plots;
using Sufni.App.ExtensionHost.RecordedSessions;
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

    [AvaloniaFact]
    public async Task SessionStatisticsPlotView_UsesAvaloniaTitleAndSuppressesScottPlotTitle()
    {
        var view = new TestableSessionStatisticsPlotView
        {
            PlotKind = PlotKind.TravelHistogram,
            SuspensionType = SuspensionType.Rear,
            TravelHistogramMode = TravelHistogramMode.DynamicSag,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        Assert.Equal("Rear travel", mounted.View.StatisticsTitle);
        Assert.False(mounted.View.PlotShowsScottPlotTitle);

        view.Telemetry = CreateProcessed();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(mounted.View.ScottPlotTitle);
    }

    [AvaloniaFact]
    public async Task SessionStatisticsPlotView_HeaderContentPushesTitleInsteadOfOverlapping()
    {
        var header = new Border
        {
            Width = 300,
            Height = 24,
        };
        var view = new TestableSessionStatisticsPlotView
        {
            Width = 360,
            PlotKind = PlotKind.Balance,
            BalanceType = BalanceType.Compression,
            HeaderContent = header,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        var title = mounted.View.StatisticsTitleTextBlock;
        var headerContent = mounted.View.StatisticsHeaderContentPresenter;
        Assert.Same(header, headerContent.Content);
        var titleTopLeft = title.TranslatePoint(default, mounted.View)!.Value;
        var headerTopLeft = headerContent.TranslatePoint(default, mounted.View)!.Value;
        var titleRight = titleTopLeft.X + title.Bounds.Width;
        var headerLeft = headerTopLeft.X;

        Assert.True(titleRight <= headerLeft);
        Assert.True(titleTopLeft.X + title.Bounds.Width / 2 < mounted.View.Bounds.Width / 2);
    }

    [AvaloniaFact]
    public async Task SessionStatisticsPlotView_HeaderContentKeepsTitleCentered_WhenSpaceAllows()
    {
        var header = new Border
        {
            Width = 300,
            Height = 24,
        };
        var view = new TestableSessionStatisticsPlotView
        {
            Width = 900,
            PlotKind = PlotKind.Balance,
            BalanceType = BalanceType.Compression,
            HeaderContent = header,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        var title = mounted.View.StatisticsTitleTextBlock;
        var headerContent = mounted.View.StatisticsHeaderContentPresenter;
        Assert.Same(header, headerContent.Content);
        var titleTopLeft = title.TranslatePoint(default, mounted.View)!.Value;
        var headerTopLeft = headerContent.TranslatePoint(default, mounted.View)!.Value;
        var titleCenter = titleTopLeft.X + title.Bounds.Width / 2;
        var viewCenter = mounted.View.Bounds.Width / 2;
        var titleRight = titleTopLeft.X + title.Bounds.Width;

        Assert.True(Math.Abs(titleCenter - viewCenter) < 1);
        Assert.True(titleRight < headerTopLeft.X);
    }

    [AvaloniaFact]
    public async Task SessionStatisticsPlotView_AppliesExtensionOverlayDescriptors_WhenContributionsChangeAndTelemetryReloads()
    {
        var slots = new RecordedSessionExtensionSlots();
        var view = new TestableSessionStatisticsPlotView
        {
            PlotKind = PlotKind.TravelHistogram,
            SuspensionType = SuspensionType.Front,
            ExtensionSlots = slots,
            Telemetry = CreateProcessed(),
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Empty(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        Assert.Empty(plot.Plot.PlottableList.OfType<Scatter>());

        slots.StatisticsOverlays.Add(new RecordedSessionStatisticsOverlayContribution(
            "extension",
            "rear-overlay",
            Order: 0,
            RecordedSessionStatisticsPlotTarget.TravelHistogram(SuspensionType.Rear),
            ViewModel: null,
            new RecordedSessionStatisticsPlotOverlayDescriptor(
                [new RecordedSessionPlotLineOverlay(1, 2, 3, 4, CreateOverlayStyle())],
                [new RecordedSessionPlotBandOverlay(5, 6, CreateOverlayStyle())],
                Labels: [])));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        Assert.Empty(plot.Plot.PlottableList.OfType<Scatter>());

        slots.StatisticsOverlays.Add(new RecordedSessionStatisticsOverlayContribution(
            "extension",
            "front-overlay",
            Order: 1,
            RecordedSessionStatisticsPlotTarget.TravelHistogram(SuspensionType.Front),
            ViewModel: null,
            new RecordedSessionStatisticsPlotOverlayDescriptor(
                [new RecordedSessionPlotLineOverlay(2, 12, 8, 18, CreateOverlayStyle(width: 3))],
                [new RecordedSessionPlotBandOverlay(20, 30, CreateOverlayStyle(opacity: 0.35))],
                Labels:
                [
                    new RecordedSessionPlotLabelOverlay(
                        X: 0,
                        Y: 18,
                        "match avg: 18.0 mm",
                        new RecordedSessionPlotLabelStyle(
                            new RecordedSessionMapColor(255, 17, 34, 51),
                            BackgroundColor: null,
                            FontSize: 11,
                            RecordedSessionPlotLabelAnchor.Top),
                        RecordedSessionPlotLabelPlacement.PlotRightEdge)
                ])));
        await ViewTestHelpers.FlushDispatcherAsync();

        AssertStatisticsOverlay(plot);

        view.Telemetry = CreateProcessed();
        await ViewTestHelpers.FlushDispatcherAsync();

        AssertStatisticsOverlay(plot);
    }

    [AvaloniaFact]
    public async Task SessionStatisticsPlotView_ForwardsSelectedRangeSelectionToSelectablePlot()
    {
        var telemetry = CreateProcessed();
        var selection = CreateFrontDampingSelection(telemetry);
        var view = new TestableSessionStatisticsPlotView
        {
            PlotKind = PlotKind.VelocityHistogram,
            SuspensionType = SuspensionType.Front,
            Telemetry = telemetry,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.SelectedRangeSelection = selection;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Contains(GetBars(plot.Plot), bar => bar.LineWidth == 3.0f);
    }

    private static RecordedSessionPlotOverlayStyle CreateOverlayStyle(double width = 2, double opacity = 0.75)
    {
        return new RecordedSessionPlotOverlayStyle(
            new RecordedSessionMapColor(255, 17, 34, 51),
            width,
            opacity);
    }

    private static void AssertStatisticsOverlay(ScottPlot.Avalonia.AvaPlot plot)
    {
        var span = Assert.Single(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        Assert.Equal(20, span.X1, 3);
        Assert.Equal(30, span.X2, 3);
        Assert.True(span.IsVisible);
        Assert.False(span.EnableAutoscale);

        var line = Assert.Single(plot.Plot.PlottableList.OfType<Scatter>());
        Assert.False(line.MarkerStyle.IsVisible);
        Assert.Equal(3, line.LineStyle.Width, 3);
        Assert.Contains(
            "match avg: 18.0 mm",
            plot.Plot.PlottableList.OfType<Text>().SelectMany(PlotTestHelpers.ReadTextLabels));
    }

    private static DampingRangeSelection CreateFrontDampingSelection(TelemetryData telemetry)
    {
        var histogram = TelemetryStatistics.CalculateVelocityHistogram(telemetry, SuspensionType.Front);
        for (var velocityBinIndex = 0; velocityBinIndex < histogram.Values.Count; velocityBinIndex++)
        {
            var travelValues = histogram.Values[velocityBinIndex];
            for (var travelBinIndex = 0; travelBinIndex < travelValues.Length; travelBinIndex++)
            {
                if (travelValues[travelBinIndex] > 0)
                {
                    return new DampingRangeSelection(
                        SuspensionType.Front,
                        VelocityAverageMode.SampleAveraged,
                        velocityBinIndex,
                        travelBinIndex,
                        travelBinIndex);
                }
            }
        }

        Assert.Fail("Expected a non-empty front damping histogram bin.");
        return default!;
    }

    private static Bar[] GetBars(Plot plot)
    {
        var bars = new List<Bar>();
        foreach (var plottable in plot.PlottableList)
        {
            if (plottable is Bar bar)
            {
                bars.Add(bar);
                continue;
            }

            if (plottable.GetType().GetProperty("Bars")?.GetValue(plottable) is IEnumerable<Bar> nestedBars)
            {
                bars.AddRange(nestedBars);
            }
        }

        return [.. bars];
    }

    private sealed class TestableSessionStatisticsPlotView : SessionStatisticsPlotView
    {
        private TextBlock? statisticsTitleTextBlock;
        private ContentControl? statisticsHeaderContentPresenter;

        public Type PlotModelType => PlotModel.GetType();
        public TelemetryTimeRange? PlotAnalysisRange => PlotModel.AnalysisRange;
        public bool PlotShowsScottPlotTitle => PlotModel.ShowTitle;
        public string ScottPlotTitle => PlotControl.Plot.Axes.Title.Label.Text;
        public TextBlock StatisticsTitleTextBlock => statisticsTitleTextBlock!;
        public ContentControl StatisticsHeaderContentPresenter => statisticsHeaderContentPresenter!;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            statisticsTitleTextBlock = e.NameScope.Find<TextBlock>("StatisticsTitleTextBlock");
            statisticsHeaderContentPresenter = e.NameScope.Find<ContentControl>("StatisticsHeaderContentPresenter");
        }
    }
}
