using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Items;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Views;

public class LiveGraphPlotDesktopViewTests
{
    [AvaloniaFact]
    public async Task LiveSessionGraphDesktopView_WiresLivePlotViews_AndAppendsGraphBatches()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var batches = new Subject<LiveGraphBatch>();
        var workspace = new StubLiveSessionGraphWorkspace(batches, hasPitchRollSection: true);
        var view = new LiveSessionGraphDesktopView
        {
            DataContext = workspace
        };
        var host = new Window
        {
            Width = 1200,
            Height = 900,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        var travelView = GetNamedVisual<LiveTravelPlotDesktopView>(view, "TravelPlot");
        var velocityView = GetNamedVisual<LiveVelocityPlotDesktopView>(view, "VelocityPlot");
        var imuView = GetNamedVisual<LiveImuPlotDesktopView>(view, "ImuPlot");
        var pitchRollView = GetNamedVisual<LiveFramePitchRollPlotDesktopView>(view, "PitchRollPlot");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.NotNull(pitchRollView);

        batches.OnNext(CreateBatch(revision: 1));
        await FlushGraphBatchesAsync(travelView!, velocityView!, imuView!, pitchRollView!);

        var travelPlot = GetRenderedPlot(travelView!);
        var velocityPlot = GetRenderedPlot(velocityView!);
        var imuPlot = GetRenderedPlot(imuView!);
        var pitchRollPlot = GetRenderedPlot(pitchRollView!);

        Assert.Contains(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => streamer.Data.CountTotal > 0);
        Assert.Contains(velocityPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => streamer.Data.CountTotal > 0);
        Assert.Contains(imuPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => streamer.Data.CountTotal > 0);
        Assert.Contains(pitchRollPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => streamer.Data.CountTotal > 0);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task LiveSessionGraphDesktopView_ContinuesUpdating_AfterDetachReattachCycle()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var batches = new Subject<LiveGraphBatch>();
        var workspace = new StubLiveSessionGraphWorkspace(batches);
        var view = new LiveSessionGraphDesktopView
        {
            DataContext = workspace
        };
        var host = new Window
        {
            Width = 1200,
            Height = 900,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        var travelView = GetNamedVisual<LiveTravelPlotDesktopView>(view, "TravelPlot");
        Assert.NotNull(travelView);

        batches.OnNext(CreateBatch(revision: 1));
        await FlushGraphBatchesAsync(travelView!);

        var travelPlot = GetRenderedPlot(travelView!);
        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));

        host.Content = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        host.Content = view;
        await ViewTestHelpers.FlushDispatcherAsync();

        batches.OnNext(CreateBatch(revision: 2));
        await FlushGraphBatchesAsync(travelView!);

        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(6, streamer.Data.CountTotal));

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task LiveSessionGraphDesktopView_CoalescesPendingGraphBatches_ToBoundRenderWork()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var batches = new Subject<LiveGraphBatch>();
        var workspace = new StubLiveSessionGraphWorkspace(batches);
        var view = new LiveSessionGraphDesktopView
        {
            DataContext = workspace
        };
        var host = new Window
        {
            Width = 1200,
            Height = 900,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        var travelView = GetNamedVisual<LiveTravelPlotDesktopView>(view, "TravelPlot");
        Assert.NotNull(travelView);

        for (var revision = 1; revision <= 6; revision++)
        {
            batches.OnNext(CreateBatch(revision, sampleCount: 600, startOffset: (revision - 1) * 600));
        }

        await FlushGraphBatchesAsync(travelView!);

        var travelPlot = GetRenderedPlot(travelView!);
        Assert.All(
            travelPlot.Plot.PlottableList.OfType<DataStreamer>(),
            streamer => Assert.Equal(2560, streamer.Data.CountTotal));

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task LiveSessionGraphDesktopView_UsesNestedTelemetryRows()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var workspace = new StubLiveSessionGraphWorkspace(
            new Subject<LiveGraphBatch>(),
            hasTravelSection: true,
            hasVelocitySection: true,
            hasImuSection: true,
            hasSpeedSection: true);
        var view = new LiveSessionGraphDesktopView
        {
            DataContext = workspace
        };
        var host = new Window
        {
            Width = 1200,
            Height = 900,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        var root = GetGraphRoot(view);
        Assert.Equal(
            ["Travel (mm)", "Vibration RMS (g)", "GPS speed (km/h)"],
            root.Rows.Select(row => row.Title!).ToArray());

        var travelRow = GetBaseRow(root, "Travel (mm)");
        var imuRow = GetBaseRow(root, "Vibration RMS (g)");
        var gpsRow = GetBaseRow(root, "GPS speed (km/h)");

        Assert.Equal(["Velocity (m/s)"], travelRow.ChildRows.Select(row => row.Title!).ToArray());
        Assert.Equal(["Frame pitch/roll (deg)"], imuRow.ChildRows.Select(row => row.Title!).ToArray());
        Assert.Equal(["Elevation (m)"], gpsRow.ChildRows.Select(row => row.Title!).ToArray());

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static TelemetryPlotsRoot GetGraphRoot(LiveSessionGraphDesktopView view)
    {
        var root = view.GetVisualDescendants()
            .OfType<TelemetryPlotsRoot>()
            .SingleOrDefault(root => root.Name == "GraphRoot");
        Assert.NotNull(root);
        return root!;
    }

    private static T GetNamedVisual<T>(Control root, string name)
        where T : Control
    {
        var rowsView = Assert.Single(root.GetVisualDescendants().OfType<LiveSessionGraphRowsView>());
        var visual = rowsView.FindControl<T>(name);
        Assert.NotNull(visual);
        return visual!;
    }

    private static TelemetryPlotRow GetBaseRow(TelemetryPlotsRoot root, string title)
        => Assert.Single(root.Rows, row => row.Title == title);

    private static TelemetryPlotRow GetChildRow(TelemetryPlotRow row, string title)
        => Assert.Single(row.ChildRows, child => child.Title == title);

    private static AvaPlot GetRenderedPlot(Control view) =>
        Assert.Single(view.GetVisualDescendants().OfType<AvaPlot>());

    private static LiveGraphBatch CreateBatch(long revision, int sampleCount = 3, int startOffset = 0)
    {
        var times = Enumerable.Range(startOffset, sampleCount)
            .Select(index => index * 0.01)
            .ToArray();
        var frontTravel = Enumerable.Range(startOffset, sampleCount)
            .Select(index => 10.0 + index)
            .ToArray();
        var rearTravel = Enumerable.Range(startOffset, sampleCount)
            .Select(index => 9.0 + index)
            .ToArray();
        var frontVelocity = Enumerable.Range(startOffset, sampleCount)
            .Select(index => 1000.0 + index * 10.0)
            .ToArray();
        var rearVelocity = Enumerable.Range(startOffset, sampleCount)
            .Select(index => 900.0 + index * 10.0)
            .ToArray();

        return new LiveGraphBatch(
            Revision: revision,
            TravelTimes: times,
            FrontTravel: frontTravel,
            RearTravel: rearTravel,
            VelocityTimes: times,
            FrontVelocity: frontVelocity,
            RearVelocity: rearVelocity,
            ImuTimes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>
            {
                [LiveImuLocation.Frame] = [times[^1]],
            },
            ImuVibrationRms: new Dictionary<LiveImuLocation, IReadOnlyList<double>>
            {
                [LiveImuLocation.Frame] = [1.5],
            },
            FramePitchRollTimes: times,
            FramePitchDegrees: Enumerable.Range(startOffset, sampleCount)
                .Select(index => 1.0 + index)
                .ToArray(),
            FrameRollDegrees: Enumerable.Range(startOffset, sampleCount)
                .Select(index => -1.0 - index)
                .ToArray());
    }

    private static async Task FlushGraphBatchesAsync(params LiveGraphPlotDesktopViewBase[] views)
    {
        foreach (var view in views)
        {
            view.FlushPendingGraphBatches();
        }

        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private sealed class StubLiveSessionGraphWorkspace(
        Subject<LiveGraphBatch> graphBatches,
        bool hasTravelSection = true,
        bool hasVelocitySection = true,
        bool hasImuSection = true,
        bool hasPitchRollSection = false,
        bool hasSpeedSection = false,
        bool hasElevationSection = false) : ILiveSessionGraphWorkspace
    {
        public IObservable<LiveGraphBatch> GraphBatches { get; } = graphBatches;
        public LiveSessionPlotRanges PlotRanges { get; } = new(180, 5, 5);
        public IReadOnlyList<TrackPoint> TrackPoints { get; } =
        [
            new TrackPoint(0, 0, 0, 100, 5),
            new TrackPoint(1, 1, 1, 101, 6),
        ];
        public TrackTimeRange? TrackTimelineContext { get; } = new(0, 1);
        public SurfacePresentationState TravelGraphState { get; } = hasTravelSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState VelocityGraphState { get; } = hasVelocitySection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState ImuGraphState { get; } = hasImuSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState PitchRollGraphState { get; } = hasPitchRollSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState SpeedGraphState { get; } = hasSpeedSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState ElevationGraphState { get; } = hasElevationSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SessionPlotPreferences PlotPreferences { get; } = new();
        public SessionGraphPreferences GraphPreferences { get; set; } = SessionGraphPreferences.Default;
        public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();
        public SessionTimelineLinkViewModel Timeline { get; } = new();
    }
}
