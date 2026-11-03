using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Sufni.App.Shared.Views;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetryData;
using static Sufni.App.Tests.TestSupport.Fixtures.PlotTestHelpers;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Plots.Views.Plots;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Plots.Views.Plots;

[Collection("Ui")]
public class TimeSeriesPlotPlaybackRequestTests
{
    [AvaloniaFact]
    public async Task TravelPlotView_SpaceOverPlotWithCursor_RequestsTimelinePlaybackToggle()
    {
        var timeline = new SessionTimelineLinkViewModel();
        var toggleRequests = 0;
        timeline.PlaybackToggleRequested += (_, _) => toggleRequests++;
        var view = new TravelPlotView { Timeline = timeline };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        RenderPlotInMemory(plot);
        var overPlot = plot.TranslatePoint(GetDataAreaCenterPoint(plot), mounted.Host);
        Assert.NotNull(overPlot);
        mounted.Host.MouseMove(overPlot.Value);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.NotNull(timeline.NormalizedCursorPosition);

        mounted.Host.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(1, toggleRequests);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_SpaceWithoutPointerOverPlot_DoesNotRequestToggle()
    {
        var timeline = new SessionTimelineLinkViewModel();
        var toggleRequests = 0;
        timeline.PlaybackToggleRequested += (_, _) => toggleRequests++;
        var view = new TravelPlotView { Timeline = timeline };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        timeline.SetCursorPosition(0.25);
        mounted.Host.MouseMove(new Point(-10, -10));
        mounted.Host.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(0, toggleRequests);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_ClickInsidePlot_RequestsTimelinePlaybackStop()
    {
        using var scheduler = new ManualUiTimerScheduler();
        var timeline = new SessionTimelineLinkViewModel();
        var stopRequests = 0;
        timeline.PlaybackStopRequested += (_, _) => stopRequests++;
        var view = new TravelPlotView { Timeline = timeline };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        UiTimers.SetScheduler(mounted.Host, scheduler);
        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        RenderPlotInMemory(plot);
        var overPlot = plot.TranslatePoint(GetDataAreaCenterPoint(plot), mounted.Host);
        Assert.NotNull(overPlot);
        mounted.Host.MouseDown(overPlot.Value, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseUp(overPlot.Value, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal(0, stopRequests);

        FireScheduledCallbacks(scheduler);

        Assert.Equal(1, stopRequests);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_DragInsidePlot_DoesNotRequestPlaybackStop()
    {
        using var scheduler = new ManualUiTimerScheduler();
        var timeline = new SessionTimelineLinkViewModel();
        var stopRequests = 0;
        timeline.PlaybackStopRequested += (_, _) => stopRequests++;
        var view = new TravelPlotView { Timeline = timeline };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        UiTimers.SetScheduler(mounted.Host, scheduler);
        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        RenderPlotInMemory(plot);
        var overPlot = plot.TranslatePoint(GetDataAreaCenterPoint(plot), mounted.Host);
        Assert.NotNull(overPlot);
        var dragTarget = overPlot.Value + new Point(30, 0);
        mounted.Host.MouseDown(overPlot.Value, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseMove(dragTarget);
        mounted.Host.MouseUp(dragTarget, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        FireScheduledCallbacks(scheduler);
        Assert.Equal(0, stopRequests);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_PointerMove_DoesNotMoveCursor_WhilePlaybackIsActive()
    {
        var timeline = new SessionTimelineLinkViewModel();
        var view = new TravelPlotView { Timeline = timeline };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        timeline.SetPlaybackActive(true);
        timeline.SetCursorPosition(0.25);

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        RenderPlotInMemory(plot);
        var overPlot = plot.TranslatePoint(GetDataAreaCenterPoint(plot), mounted.Host);
        Assert.NotNull(overPlot);
        mounted.Host.MouseMove(overPlot.Value);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(0.25, timeline.NormalizedCursorPosition);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_ClickDuringPlayback_StopsAndPlacesCursorAtClick()
    {
        using var scheduler = new ManualUiTimerScheduler();
        var timeline = new SessionTimelineLinkViewModel();
        // Mimic the playback owner: a stop request deactivates playback, so
        // the same click may place the cursor again.
        timeline.PlaybackStopRequested += (_, _) => timeline.SetPlaybackActive(false);
        var view = new TravelPlotView { Timeline = timeline };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        UiTimers.SetScheduler(mounted.Host, scheduler);
        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        timeline.SetPlaybackActive(true);
        timeline.SetCursorPosition(0.95);

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        RenderPlotInMemory(plot);
        var overPlot = plot.TranslatePoint(GetDataAreaCenterPoint(plot), mounted.Host);
        Assert.NotNull(overPlot);
        mounted.Host.MouseDown(overPlot.Value, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseUp(overPlot.Value, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(timeline.IsPlaybackActive);
        Assert.Equal(0.95, timeline.NormalizedCursorPosition);

        FireScheduledCallbacks(scheduler);

        Assert.False(timeline.IsPlaybackActive);
        Assert.NotNull(timeline.NormalizedCursorPosition);
        Assert.Equal(0.5, timeline.NormalizedCursorPosition!.Value, precision: 2);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_DoubleClick_CancelsPendingStop()
    {
        using var scheduler = new ManualUiTimerScheduler();
        var timeline = new SessionTimelineLinkViewModel();
        var stopRequests = 0;
        timeline.PlaybackStopRequested += (_, _) => stopRequests++;
        var view = new TravelPlotView
        {
            Timeline = timeline,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        UiTimers.SetScheduler(mounted.Host, scheduler);
        view.Telemetry = CreateMinimal(duration: 10);
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        RenderPlotInMemory(plot);
        var overPlot = plot.TranslatePoint(GetDataAreaCenterPoint(plot), mounted.Host);
        Assert.NotNull(overPlot);
        mounted.Host.MouseDown(overPlot.Value, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseUp(overPlot.Value, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        mounted.Host.MouseDown(overPlot.Value, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();
        mounted.Host.MouseUp(overPlot.Value, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        FireScheduledCallbacks(scheduler);
        Assert.Equal(0, stopRequests);
    }

    private static void FireScheduledCallbacks(ManualUiTimerScheduler scheduler)
    {
        // Fire even when the gesture correctly canceled every pending callback.
        using var tick = scheduler.ScheduleOnce(TimeSpan.FromSeconds(1), () => { });
        scheduler.FireAll();
    }
}
