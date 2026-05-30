using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
