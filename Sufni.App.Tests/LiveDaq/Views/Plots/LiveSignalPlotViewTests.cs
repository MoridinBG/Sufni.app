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
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.DesktopViews.Items;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Views.Plots;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.LiveDaq.Views.Controls;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.LiveDaq.Views.Plots;

[Collection("Ui")]
public class LiveSignalPlotViewTests
{
    [AvaloniaFact]
    public async Task LiveSessionSignalsDesktopView_WiresLivePlotViews_AndAppendsSignalBatches()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var batches = new Subject<LiveSignalBatch>();
        var workspace = new StubLiveSessionSignalsWorkspace(batches, hasPitchRollSection: true);
        var view = new LiveSessionSignalsDesktopView
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

        var travelView = GetNamedVisual<LiveTravelPlotView>(view, "TravelPlot");
        var velocityView = GetNamedVisual<LiveVelocityPlotView>(view, "VelocityPlot");
        var imuView = GetNamedVisual<LiveImuPlotView>(view, "ImuPlot");
        var pitchRollView = GetNamedVisual<LiveFramePitchRollPlotView>(view, "PitchRollPlot");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.NotNull(pitchRollView);

        batches.OnNext(CreateBatch(revision: 1));
        await FlushSignalBatchesAsync(travelView!, velocityView!, imuView!, pitchRollView!);

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
    public async Task LiveSessionSignalsDesktopView_ContinuesUpdating_AfterDetachReattachCycle()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var batches = new Subject<LiveSignalBatch>();
        var workspace = new StubLiveSessionSignalsWorkspace(batches);
        var view = new LiveSessionSignalsDesktopView
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

        var travelView = GetNamedVisual<LiveTravelPlotView>(view, "TravelPlot");
        Assert.NotNull(travelView);

        batches.OnNext(CreateBatch(revision: 1));
        await FlushSignalBatchesAsync(travelView!);

        var travelPlot = GetRenderedPlot(travelView!);
        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));

        host.Content = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        host.Content = view;
        await ViewTestHelpers.FlushDispatcherAsync();

        batches.OnNext(CreateBatch(revision: 2));
        await FlushSignalBatchesAsync(travelView!);

        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(6, streamer.Data.CountTotal));

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task LiveSessionSignalsDesktopView_CoalescesPendingSignalBatches_ToBoundRenderWork()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var batches = new Subject<LiveSignalBatch>();
        var workspace = new StubLiveSessionSignalsWorkspace(batches);
        var view = new LiveSessionSignalsDesktopView
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

        var travelView = GetNamedVisual<LiveTravelPlotView>(view, "TravelPlot");
        Assert.NotNull(travelView);

        for (var revision = 1; revision <= 6; revision++)
        {
            batches.OnNext(CreateBatch(revision, sampleCount: 600, startOffset: (revision - 1) * 600));
        }

        await FlushSignalBatchesAsync(travelView!);

        var travelPlot = GetRenderedPlot(travelView!);
        Assert.All(
            travelPlot.Plot.PlottableList.OfType<DataStreamer>(),
            streamer => Assert.Equal(2560, streamer.Data.CountTotal));

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task LiveSessionSignalsDesktopView_UsesNestedTelemetryRows()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var workspace = new StubLiveSessionSignalsWorkspace(
            new Subject<LiveSignalBatch>(),
            hasTravelSection: true,
            hasVelocitySection: true,
            hasImuSection: true,
            hasSpeedSection: true);
        var view = new LiveSessionSignalsDesktopView
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

        var root = GetSignalRowsRoot(view);
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

    private static SignalRowsRoot GetSignalRowsRoot(LiveSessionSignalsDesktopView view)
    {
        var root = view.GetVisualDescendants()
            .OfType<SignalRowsRoot>()
            .SingleOrDefault(root => root.Name == "SignalRowsRoot");
        Assert.NotNull(root);
        return root!;
    }

    private static T GetNamedVisual<T>(Control root, string name)
        where T : Control
    {
        var rowsView = Assert.Single(root.GetVisualDescendants().OfType<LiveSignalRowsView>());
        var visual = rowsView.FindControl<T>(name);
        Assert.NotNull(visual);
        return visual!;
    }

    private static SignalRow GetBaseRow(SignalRowsRoot root, string title)
        => Assert.Single(root.Rows, row => row.Title == title);

    private static SignalRow GetChildRow(SignalRow row, string title)
        => Assert.Single(row.ChildRows, child => child.Title == title);

    private static AvaPlot GetRenderedPlot(Control view) =>
        Assert.Single(view.GetVisualDescendants().OfType<AvaPlot>());

    private static LiveSignalBatch CreateBatch(long revision, int sampleCount = 3, int startOffset = 0)
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

        return new LiveSignalBatch(
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

    private static async Task FlushSignalBatchesAsync(params LiveSignalPlotViewBase[] views)
    {
        foreach (var view in views)
        {
            view.FlushPendingSignalBatches();
        }

        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private sealed class StubLiveSessionSignalsWorkspace(
        Subject<LiveSignalBatch> signalBatches,
        bool hasTravelSection = true,
        bool hasVelocitySection = true,
        bool hasImuSection = true,
        bool hasPitchRollSection = false,
        bool hasSpeedSection = false,
        bool hasElevationSection = false) : ILiveSessionSignalsWorkspace
    {
        public IObservable<LiveSignalBatch> SignalBatches { get; } = signalBatches;
        public LiveSessionPlotRanges PlotRanges { get; } = new(180, 5, 5);
        public IReadOnlyList<TrackPoint> TrackPoints { get; } =
        [
            new TrackPoint(0, 0, 0, 100, 5),
            new TrackPoint(1, 1, 1, 101, 6),
        ];
        public TrackTimeRange? TrackTimelineContext { get; } = new(0, 1);
        public SurfacePresentationState TravelSignalState { get; } = hasTravelSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState VelocitySignalState { get; } = hasVelocitySection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState ImuSignalState { get; } = hasImuSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState PitchRollSignalState { get; } = hasPitchRollSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState SpeedSignalState { get; } = hasSpeedSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState ElevationSignalState { get; } = hasElevationSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SignalDisplayPreferences SignalDisplayPreferences { get; } = new();
        public SignalLayoutPreferences SignalLayoutPreferences { get; set; } = SignalLayoutPreferences.Default;
        public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();
        public SessionTimelineLinkViewModel Timeline { get; } = new();
    }
}
