using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia;
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
using AvaloniaColor = Avalonia.Media.Color;

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

        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));
        Assert.All(velocityPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));
        Assert.Contains(imuPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => streamer.Data.CountTotal == 1);
        Assert.All(pitchRollPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));
        Assert.Empty(travelPlot.Plot.Axes.Title.Label.Text);
        Assert.Empty(velocityPlot.Plot.Axes.Title.Label.Text);
        Assert.Empty(imuPlot.Plot.Axes.Title.Label.Text);
        Assert.Empty(pitchRollPlot.Plot.Axes.Title.Label.Text);
        Assert.True(travelPlot.Plot.Legend.IsVisible);
        Assert.True(velocityPlot.Plot.Legend.IsVisible);
        Assert.True(imuPlot.Plot.Legend.IsVisible);
        Assert.True(pitchRollPlot.Plot.Legend.IsVisible);
        Assert.Equal(
            ["Front", "Rear"],
            travelPlot.Plot.PlottableList.OfType<DataStreamer>().Select(streamer => streamer.LegendText).ToArray());
        Assert.Equal(
            ["Front", "Rear"],
            velocityPlot.Plot.PlottableList.OfType<DataStreamer>().Select(streamer => streamer.LegendText).ToArray());
        Assert.Equal(
            ["Frame", "Fork", "Shock"],
            imuPlot.Plot.PlottableList.OfType<DataStreamer>().Select(streamer => streamer.LegendText).ToArray());
        Assert.Equal(
            ["Pitch", "Roll"],
            pitchRollPlot.Plot.PlottableList.OfType<DataStreamer>().Select(streamer => streamer.LegendText).ToArray());
        Assert.Empty(travelPlot.Plot.Axes.Bottom.Label.Text);
        Assert.Empty(travelPlot.Plot.Axes.Left.Label.Text);
        Assert.Empty(velocityPlot.Plot.Axes.Bottom.Label.Text);
        Assert.Empty(velocityPlot.Plot.Axes.Left.Label.Text);
        Assert.Empty(imuPlot.Plot.Axes.Bottom.Label.Text);
        Assert.Empty(imuPlot.Plot.Axes.Left.Label.Text);
        Assert.Empty(pitchRollPlot.Plot.Axes.Bottom.Label.Text);
        Assert.Empty(pitchRollPlot.Plot.Axes.Left.Label.Text);
        Assert.True(travelPlot.Plot.Axes.Right.IsVisible);
        Assert.True(velocityPlot.Plot.Axes.Right.IsVisible);
        Assert.True(imuPlot.Plot.Axes.Right.IsVisible);
        Assert.True(pitchRollPlot.Plot.Axes.Right.IsVisible);
        // Live plots auto-size around the largest value seen so far (with 10% headroom),
        // falling back to a per-metric floor when running max is below it.
        Assert.Equal(13.2, travelPlot.Plot.Axes.Left.Max, precision: 4);
        Assert.Equal(0, travelPlot.Plot.Axes.Left.Min);
        Assert.Equal(1.122, velocityPlot.Plot.Axes.Left.Max, precision: 4);
        Assert.Equal(-1.122, velocityPlot.Plot.Axes.Left.Min, precision: 4);
        Assert.Equal(1.65, imuPlot.Plot.Axes.Left.Max, precision: 4);
        Assert.Equal(0, imuPlot.Plot.Axes.Left.Min);
        Assert.Equal(5, pitchRollPlot.Plot.Axes.Left.Max);
        Assert.Equal(-5, pitchRollPlot.Plot.Axes.Left.Min);
        Assert.Equal(travelPlot.Plot.Axes.Left.Min, travelPlot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(travelPlot.Plot.Axes.Left.Max, travelPlot.Plot.Axes.Right.Max, precision: 6);
        Assert.Equal(velocityPlot.Plot.Axes.Left.Min, velocityPlot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(velocityPlot.Plot.Axes.Left.Max, velocityPlot.Plot.Axes.Right.Max, precision: 6);
        Assert.Equal(imuPlot.Plot.Axes.Left.Min, imuPlot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(imuPlot.Plot.Axes.Left.Max, imuPlot.Plot.Axes.Right.Max, precision: 6);
        Assert.Equal(pitchRollPlot.Plot.Axes.Left.Min, pitchRollPlot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(pitchRollPlot.Plot.Axes.Left.Max, pitchRollPlot.Plot.Axes.Right.Max, precision: 6);

        batches.OnNext(LiveGraphBatch.Empty with { Revision = 2 });
        await FlushGraphBatchesAsync(travelView!, velocityView!, imuView!, pitchRollView!);

        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(0, streamer.Data.CountTotal));
        Assert.All(pitchRollPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(0, streamer.Data.CountTotal));
        // After a reset batch the running max clears, so axes shrink back to the floor.
        Assert.Equal(5, travelPlot.Plot.Axes.Left.Max);
        Assert.Equal(0.5, velocityPlot.Plot.Axes.Left.Max);
        Assert.Equal(-0.5, velocityPlot.Plot.Axes.Left.Min);
        Assert.Equal(1, imuPlot.Plot.Axes.Left.Max);
        Assert.Equal(5, pitchRollPlot.Plot.Axes.Left.Max);
        Assert.Equal(-5, pitchRollPlot.Plot.Axes.Left.Min);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task LiveTravelPlotDesktopView_AppliesPlotBackgroundProperties()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new LiveTravelPlotDesktopView
        {
            PlotFigureBackground = AvaloniaColor.Parse("#101820"),
            PlotDataBackground = AvaloniaColor.Parse("#203040"),
        };
        var host = new Window
        {
            Width = 800,
            Height = 400,
            Content = view
        };

        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = GetRenderedPlot(view);
        Assert.Equal(ScottPlot.Color.FromHex("#101820"), plot.Plot.FigureBackground.Color);
        Assert.Equal(ScottPlot.Color.FromHex("#203040"), plot.Plot.DataBackground.Color);

        view.PlotFigureBackground = AvaloniaColor.Parse("#111213");
        view.PlotDataBackground = AvaloniaColor.Parse("#212223");
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(ScottPlot.Color.FromHex("#111213"), plot.Plot.FigureBackground.Color);
        Assert.Equal(ScottPlot.Color.FromHex("#212223"), plot.Plot.DataBackground.Color);

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
    public async Task LiveSessionGraphDesktopView_CollapsesTravelRows_WhenTravelSectionUnavailable()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var workspace = new StubLiveSessionGraphWorkspace(new Subject<LiveGraphBatch>(), hasTravelSection: false, hasImuSection: true);
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
        var travelRow = GetBaseRow(root, "Travel (mm)");
        var velocityRow = GetChildRow(travelRow, "Velocity (m/s)");
        var imuRow = GetBaseRow(root, "Vibration RMS (g)");
        var travelView = GetNamedVisual<LiveTravelPlotDesktopView>(view, "TravelPlot");
        var velocityView = GetNamedVisual<LiveVelocityPlotDesktopView>(view, "VelocityPlot");
        var imuView = GetNamedVisual<LiveImuPlotDesktopView>(view, "ImuPlot");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.True(travelRow.IsVisible);
        Assert.True(velocityRow.IsVisible);
        Assert.True(imuRow.IsVisible);
        Assert.Equal(SurfacePresentationState.Hidden, travelRow.PresentationState);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task LiveSessionGraphDesktopView_CollapsesVelocityRows_WhenVelocitySectionUnavailable()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var workspace = new StubLiveSessionGraphWorkspace(
            new Subject<LiveGraphBatch>(),
            hasTravelSection: true,
            hasVelocitySection: false,
            hasImuSection: true);
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
        var travelRow = GetBaseRow(root, "Travel (mm)");
        var velocityRow = GetChildRow(travelRow, "Velocity (m/s)");
        var imuRow = GetBaseRow(root, "Vibration RMS (g)");

        Assert.True(travelRow.IsVisible);
        Assert.False(velocityRow.IsVisible);
        Assert.True(imuRow.IsVisible);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task LiveSessionGraphDesktopView_CollapsesImuRows_WhenImuSectionUnavailable()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var workspace = new StubLiveSessionGraphWorkspace(new Subject<LiveGraphBatch>(), hasTravelSection: true, hasImuSection: false);
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
        var imuRow = GetBaseRow(root, "Vibration RMS (g)");
        var imuView = GetNamedVisual<LiveImuPlotDesktopView>(view, "ImuPlot");

        Assert.NotNull(imuView);
        Assert.False(imuRow.IsVisible);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task LiveSessionGraphDesktopView_ShowsGpsBaseRow_WhenImuIsHidden()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var workspace = new StubLiveSessionGraphWorkspace(
            new Subject<LiveGraphBatch>(),
            hasTravelSection: true,
            hasVelocitySection: true,
            hasImuSection: false,
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
        var travelRow = GetBaseRow(root, "Travel (mm)");
        var imuRow = GetBaseRow(root, "Vibration RMS (g)");
        var gpsRow = GetBaseRow(root, "GPS speed (km/h)");
        var elevationRow = GetChildRow(gpsRow, "Elevation (m)");
        var speedView = GetNamedVisual<TrackSignalPlotDesktopView>(view, "SpeedPlot");

        Assert.NotNull(speedView);
        Assert.True(travelRow.IsVisible);
        Assert.False(imuRow.IsVisible);
        Assert.True(gpsRow.IsVisible);
        Assert.False(elevationRow.IsVisible);

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
        public SessionTimelineLinkViewModel Timeline { get; } = new();
    }
}
