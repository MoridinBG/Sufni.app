using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Behaviors;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.SessionGraphs;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;
using AvaloniaColor = Avalonia.Media.Color;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Views.Plots;

public class TravelPlotDesktopViewTests
{
    [AvaloniaFact]
    public async Task TravelPlotDesktopView_StartsEmpty_BeforeTelemetryIsAssigned()
    {
        var view = new TravelPlotDesktopView();

        Assert.Null(view.MaximumDisplayHz);

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Empty(plot.Plot.PlottableList);
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        var view = new TravelPlotDesktopView();

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Empty(plot.Plot.Axes.Title.Label.Text);
        var signals = plot.Plot.PlottableList.OfType<Signal>().ToArray();
        Assert.Single(plot.Plot.PlottableList.OfType<VerticalLine>());
        Assert.Equal(2, signals.Length);
        Assert.All(signals, signal => Assert.Same(plot.Plot.Axes.Left, signal.Axes.YAxis));
        Assert.True(plot.Plot.Axes.Right.IsVisible);
        Assert.Equal(plot.Plot.Axes.Left.Min, plot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(plot.Plot.Axes.Left.Max, plot.Plot.Axes.Right.Max, precision: 6);
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_ShowsEmptyState_WhenTelemetryHasNoTravelData()
    {
        var view = new TravelPlotDesktopView();
        var telemetry = CreateTelemetryData();
        telemetry.Front.Present = false;
        telemetry.Rear.Present = false;

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Empty(plot.Plot.Axes.Title.Label.Text);
        Assert.Empty(plot.Plot.PlottableList.OfType<Signal>());
        Assert.Single(plot.Plot.PlottableList.OfType<Text>());
        Assert.Equal(plot.Plot.Axes.Left.Min, plot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(plot.Plot.Axes.Left.Max, plot.Plot.Axes.Right.Max, precision: 6);
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_HidesRightAxis_WhenRequested()
    {
        var view = new TravelPlotDesktopView
        {
            HideRightAxis = true,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.False(plot.Plot.Axes.Right.IsVisible);
        Assert.Equal(plot.Plot.Axes.Left.Min, plot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(plot.Plot.Axes.Left.Max, plot.Plot.Axes.Right.Max, precision: 6);
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_AppliesPlotBackgroundProperties()
    {
        var view = new TravelPlotDesktopView
        {
            PlotFigureBackground = AvaloniaColor.Parse("#101820"),
            PlotDataBackground = AvaloniaColor.Parse("#203040"),
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Equal(Color.FromHex("#101820"), plot.Plot.FigureBackground.Color);
        Assert.Equal(Color.FromHex("#203040"), plot.Plot.DataBackground.Color);

        view.PlotFigureBackground = AvaloniaColor.Parse("#111213");
        view.PlotDataBackground = AvaloniaColor.Parse("#212223");
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(Color.FromHex("#111213"), plot.Plot.FigureBackground.Color);
        Assert.Equal(Color.FromHex("#212223"), plot.Plot.DataBackground.Color);
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_AnalysisRangeUpdatesOverlayWithoutReloadingSignals()
    {
        var view = new TravelPlotDesktopView();

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        var originalSignals = plot.Plot.PlottableList.OfType<Signal>().ToArray();

        view.AnalysisRange = new TelemetryTimeRange(0.25, 0.75);
        await ViewTestHelpers.FlushDispatcherAsync();

        var updatedSignals = plot.Plot.PlottableList.OfType<Signal>().ToArray();
        var selectedSpan = Assert.Single(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        Assert.Same(originalSignals[0], updatedSignals[0]);
        Assert.Same(originalSignals[1], updatedSignals[1]);
        Assert.Equal(0.25, selectedSpan.X1, 3);
        Assert.Equal(0.75, selectedSpan.X2, 3);

        view.AnalysisRange = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(selectedSpan.IsVisible);
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_RendersMarkerLinesFromTelemetry()
    {
        var view = new TravelPlotDesktopView();
        var telemetry = CreateTelemetryData();
        telemetry.Markers = [new MarkerData(0.5), new MarkerData(1.5)];

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        var markerLines = plot.Plot.PlottableList
            .OfType<VerticalLine>()
            .Where(line => !double.IsNaN(line.Position))
            .ToArray();
        Assert.Equal(2, markerLines.Length);
        Assert.Contains(markerLines, line => line.Position == 0.5);
        Assert.Contains(markerLines, line => line.Position == 1.5);
        Assert.All(markerLines, line =>
        {
            Assert.Equal(2.0f, line.LineWidth);
            Assert.Equal(Color.FromHex("#d53e4f").WithAlpha(0.9), line.LineColor);
        });
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_CullsCollidingAirtimeLabelsAfterTelemetryLoad()
    {
        var view = new TravelPlotDesktopView();
        var telemetry = CreateTelemetryData(duration: 10);
        telemetry.Airtimes =
        [
            new Airtime { Start = 1.90, End = 2.10 },
            new Airtime { Start = 1.75, End = 2.25 },
        ];

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Equal([false, true], GetAirtimeLabels(plot.Plot).Select(label => label.IsVisible).ToArray());
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_ClearsAndReloadsTelemetryWhileHidden()
    {
        var view = new TravelPlotDesktopView();
        var oldTelemetry = CreateTelemetryData();
        oldTelemetry.Markers = [new MarkerData(0.5)];
        var freshTelemetry = CreateTelemetryData();
        freshTelemetry.Markers = [new MarkerData(0.25), new MarkerData(1.5)];

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = oldTelemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Equal(2, plot.Plot.PlottableList.OfType<VerticalLine>().Count());

        view.IsVisible = false;
        view.Telemetry = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(plot.Plot.PlottableList);

        view.Telemetry = freshTelemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(3, plot.Plot.PlottableList.OfType<VerticalLine>().Count());
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_TimelineSyncConstrainsOverscrolledFullRange()
    {
        var timeline = new SessionTimelineLinkViewModel();
        var view = new TestableTravelPlotDesktopView
        {
            Timeline = timeline,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateTelemetryData(duration: 10);
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        plot.Plot.Axes.SetLimitsX(-2, 8);
        mounted.View.UpdateTimelineRangeForTest();

        Assert.Equal(0, timeline.VisibleRangeStart, 6);
        Assert.Equal(1, timeline.VisibleRangeEnd, 6);
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_MobileLongPress_SetsAnalysisRangeBoundaryWithoutClearingOnRelease()
    {
        TestApp.SetIsDesktop(false);
        try
        {
            var telemetry = CreateTelemetryData(duration: 10);
            var workspace = new RecordedSessionGraphWorkspaceStub(telemetry);
            var view = new LongPressTravelPlotDesktopView
            {
                Telemetry = telemetry,
                GraphWorkspace = workspace,
            };
            var feedbackRequestCount = 0;
            view.AddHandler(
                HapticFeedbackBehavior.LongPressFeedbackRequestedEvent,
                (_, _) => feedbackRequestCount++);

            await using var mounted = await PlotViewTestSupport.MountAsync(view);

            var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
            var pressPoint = plot.TranslatePoint(
                new Point(plot.Bounds.Width / 2, plot.Bounds.Height / 2),
                mounted.Host);
            Assert.True(plot.Bounds.Width > 0 && plot.Bounds.Height > 0, $"Plot bounds were {plot.Bounds}.");
            Assert.NotNull(pressPoint);

            mounted.Host.MouseDown(pressPoint.Value, MouseButton.Left, RawInputModifiers.None);
            await ViewTestHelpers.FlushDispatcherAsync();

            view.TriggerLongPress();
            mounted.Host.MouseUp(pressPoint.Value, MouseButton.Left, RawInputModifiers.None);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(1, workspace.SetAnalysisRangeBoundaryCallCount);
            Assert.Equal(0, workspace.ClearAnalysisRangeCallCount);
            Assert.Equal(1, feedbackRequestCount);
            Assert.NotNull(workspace.LastAnalysisRangeBoundary);
            Assert.InRange(workspace.LastAnalysisRangeBoundary.Value, 0, telemetry.Metadata.Duration);
        }
        finally
        {
            TestApp.SetIsDesktop(true);
        }
    }

    private sealed class TestableTravelPlotDesktopView : TravelPlotDesktopView
    {
        public void UpdateTimelineRangeForTest()
        {
            UpdateTimelineRange();
        }
    }

    private sealed class LongPressTravelPlotDesktopView : TravelPlotDesktopView
    {
        private Action? scheduledLongPress;

        public void TriggerLongPress()
        {
            var callback = scheduledLongPress ?? throw new InvalidOperationException("No long press was scheduled.");
            callback();
        }

        protected override IDisposable ScheduleMobileAnalysisRangeLongPress(Action callback)
        {
            scheduledLongPress = callback;
            return new TestSubscription(() => scheduledLongPress = null);
        }
    }

    private sealed class TestSubscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private static Text[] GetAirtimeLabels(Plot plot)
    {
        return plot.PlottableList
            .OfType<Text>()
            .Where(text => ReadTextLabels(text).Any(label => label.Contains("s air")))
            .ToArray();
    }

    private static IEnumerable<string> ReadTextLabels(Text text)
    {
        return text.GetType()
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.GetValue(text) as string)
            .Where(label => !string.IsNullOrWhiteSpace(label))!;
    }

    private sealed class RecordedSessionGraphWorkspaceStub(TelemetryData telemetryData) : IRecordedSessionGraphWorkspace
    {
        public TelemetryData? TelemetryData { get; } = telemetryData;
        public TelemetryTimeRange? AnalysisRange { get; private set; }
        public IReadOnlyList<TrackPoint>? TrackPoints => null;
        public TrackTimeRange? TrackTimelineContext => null;
        public SurfacePresentationState TravelGraphState => SurfacePresentationState.Ready;
        public SurfacePresentationState VelocityGraphState => SurfacePresentationState.Hidden;
        public SurfacePresentationState ImuGraphState => SurfacePresentationState.Hidden;
        public SurfacePresentationState PitchRollGraphState => SurfacePresentationState.Hidden;
        public SurfacePresentationState SpeedGraphState => SurfacePresentationState.Hidden;
        public SurfacePresentationState ElevationGraphState => SurfacePresentationState.Hidden;
        public SessionPlotPreferences PlotPreferences { get; } = new();
        public SessionGraphPreferences GraphPreferences { get; set; } = SessionGraphPreferences.Default;
        public SessionTimelineLinkViewModel Timeline { get; } = new();
        public int ClearAnalysisRangeCallCount { get; private set; }
        public int SetAnalysisRangeBoundaryCallCount { get; private set; }
        public double? LastAnalysisRangeBoundary { get; private set; }

        public void SetAnalysisRange(double startSeconds, double endSeconds)
        {
            AnalysisRange = TelemetryTimeRange.TryCreate(startSeconds, endSeconds, out var range)
                ? range
                : null;
        }

        public void ClearAnalysisRange()
        {
            ClearAnalysisRangeCallCount++;
            AnalysisRange = null;
        }

        public void SetAnalysisRangeBoundary(double boundarySeconds)
        {
            SetAnalysisRangeBoundaryCallCount++;
            LastAnalysisRangeBoundary = boundarySeconds;
        }
    }
}
