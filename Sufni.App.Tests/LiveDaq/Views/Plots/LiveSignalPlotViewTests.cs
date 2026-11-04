using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Plots;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Plots;
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
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Views.Plots;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.DesktopViews.Items;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Views.Plots;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.LiveDaq.Views.Controls;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.LiveDaq.Views.Plots;

[Collection("Ui")]
public class LiveSignalPlotViewTests
{
    [AvaloniaFact]
    public async Task SignalBatches_ClearVisibleSamplesOnlyOnExplicitReset()
    {
        ViewTestHelpers.EnsurePlotViewStyle();
        using var batches = new Subject<LiveSignalBatch>();
        var view = new LiveSessionSignalsDesktopView { DataContext = new StubLiveSessionSignalsWorkspace(batches) };
        var host = new Window { Width = 1200, Height = 900, Content = view };
        await using var mounted = new MountedView<LiveSessionSignalsDesktopView>(host, view);
        ViewTestHelpers.ShowWindow(host);
        await ViewTestHelpers.FlushDispatcherAsync();
        var travel = GetNamedVisual<LiveTravelPlotView>(view, "TravelPlot");
        batches.OnNext(CreateBatch(1));
        await FlushSignalBatchesAsync(travel);
        var plot = GetRenderedPlot(travel);
        AssertTravelSamples(plot, [10, 11, 12]);

        batches.OnNext(LiveSignalBatch.Empty with { Revision = 2 });
        await FlushSignalBatchesAsync(travel);
        AssertTravelSamples(plot, [10, 11, 12]);

        batches.OnNext(LiveSignalBatch.Empty with { Revision = 3, Kind = LiveSignalBatchKind.Reset });
        await FlushSignalBatchesAsync(travel);
        AssertTravelSamples(plot, []);
    }

    [AvaloniaFact]
    public async Task LiveSessionSignalsDesktopView_ContinuesUpdating_AfterDetachReattachCycle()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        using var batches = new Subject<LiveSignalBatch>();
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

        await using var mounted = new MountedView<LiveSessionSignalsDesktopView>(host, view);
        ViewTestHelpers.ShowWindow(host);
        await ViewTestHelpers.FlushDispatcherAsync();

        var travelView = GetNamedVisual<LiveTravelPlotView>(view, "TravelPlot");
        Assert.NotNull(travelView);

        batches.OnNext(CreateBatch(revision: 1));
        await FlushSignalBatchesAsync(travelView!);

        var travelPlot = GetRenderedPlot(travelView!);
        AssertTravelSamples(travelPlot, [10, 11, 12]);

        host.Content = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        host.Content = view;
        await ViewTestHelpers.FlushDispatcherAsync();

        batches.OnNext(CreateBatch(revision: 2, startOffset: 3));
        await FlushSignalBatchesAsync(travelView!);

        AssertTravelSamples(travelPlot, [10, 11, 12, 13, 14, 15]);
    }

    [AvaloniaFact]
    public async Task LiveSessionSignalsDesktopView_SuspendsWhileAncestorIsHidden_AndResumesWhenVisible()
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        using var batches = new Subject<LiveSignalBatch>();
        var workspace = new StubLiveSessionSignalsWorkspace(batches);
        var view = new LiveSessionSignalsDesktopView
        {
            DataContext = workspace
        };
        var tabContent = new Border
        {
            Child = view,
        };
        var host = new Window
        {
            Width = 1200,
            Height = 900,
            Content = tabContent
        };

        await using var mounted = new MountedView<LiveSessionSignalsDesktopView>(host, view);
        ViewTestHelpers.ShowWindow(host);
        await ViewTestHelpers.FlushDispatcherAsync();

        var travelView = GetNamedVisual<LiveTravelPlotView>(view, "TravelPlot");
        batches.OnNext(CreateBatch(revision: 1));
        await FlushSignalBatchesAsync(travelView);
        var travelPlot = GetRenderedPlot(travelView);
        AssertTravelSamples(travelPlot, [10, 11, 12]);

        tabContent.IsVisible = false;
        await ViewTestHelpers.FlushDispatcherAsync();

        batches.OnNext(CreateBatch(revision: 2, startOffset: 30));
        await FlushSignalBatchesAsync(travelView);
        AssertTravelSamples(travelPlot, [10, 11, 12]);
        tabContent.IsVisible = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        batches.OnNext(CreateBatch(revision: 3, startOffset: 3));
        await FlushSignalBatchesAsync(travelView);
        AssertTravelSamples(travelPlot, [10, 11, 12, 13, 14, 15]);
    }

    [AvaloniaFact]
    public async Task LiveSessionSignalsDesktopView_AppendsSignalBatches_WhileBorrowedIntoZoomOverlay()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        using var batches = new Subject<LiveSignalBatch>();
        var workspace = new StubLiveSessionSignalsWorkspace(batches);
        var view = new LiveSessionSignalsDesktopView
        {
            DataContext = workspace
        };
        var overlay = new PlotZoomOverlayHost();
        var root = new Grid
        {
            Children =
            {
                view,
                overlay,
            },
        };
        var host = new Window
        {
            Width = 1200,
            Height = 900,
            Content = root
        };

        await using var mounted = new MountedView<LiveSessionSignalsDesktopView>(host, view);
        ViewTestHelpers.ShowWindow(host);
        await ViewTestHelpers.FlushDispatcherAsync();

        var travelView = GetNamedVisual<LiveTravelPlotView>(view, "TravelPlot");
        var zoomContainer = Assert.Single(travelView.GetVisualAncestors().OfType<PlotZoomContainer>());

        batches.OnNext(CreateBatch(revision: 1));
        await FlushSignalBatchesAsync(travelView);

        var travelPlot = GetRenderedPlot(travelView);
        AssertTravelSamples(travelPlot, [10, 11, 12]);

        zoomContainer.RaiseEvent(new PlotZoomRequestedEventArgs(zoomContainer));
        await ViewTestHelpers.FlushDispatcherAsync();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(overlay.IsZoomOpen);
        Assert.Contains(overlay, travelView.GetVisualAncestors());

        batches.OnNext(CreateBatch(revision: 2, startOffset: 3));
        await FlushSignalBatchesAsync(travelView);

        AssertTravelSamples(travelPlot, [10, 11, 12, 13, 14, 15]);
    }

    private static void AssertTravelSamples(AvaPlot plot, double[] expectedFront)
    {
        var series = plot.Plot.PlottableList.OfType<DataStreamer>().ToArray();
        Assert.NotEmpty(series);
        var front = Assert.Single(series, value => value.LegendText == "Front");
        var rear = Assert.Single(series, value => value.LegendText == "Rear");
        Assert.Equal(expectedFront.Length, front.Data.CountTotal);
        Assert.Equal(expectedFront, front.Data.Data.Take(expectedFront.Length));
        Assert.Equal(expectedFront.Select(value => value - 1), rear.Data.Data.Take(rear.Data.CountTotal));
    }

    private static T GetNamedVisual<T>(Control root, string name)
        where T : Control
    {
        var rowsView = Assert.Single(root.GetVisualDescendants().OfType<LiveSignalRowsView>());
        var visual = rowsView.FindControl<T>(name);
        Assert.NotNull(visual);
        return visual!;
    }

    private static AvaPlot GetRenderedPlot(Control view) =>
        Assert.Single(view.GetVisualDescendants().OfType<AvaPlot>());

    private static LiveSignalBatch CreateBatch(long revision, int startOffset = 0) => LiveSignalBatch.Empty with
    {
        Revision = revision,
        TravelTimes = Enumerable.Range(startOffset, 3).Select(index => index * 0.01).ToArray(),
        FrontTravel = Enumerable.Range(startOffset, 3).Select(index => 10.0 + index).ToArray(),
        RearTravel = Enumerable.Range(startOffset, 3).Select(index => 9.0 + index).ToArray(),
    };

    private static async Task FlushSignalBatchesAsync(params LiveSignalPlotViewBase[] views)
    {
        foreach (var view in views)
        {
            view.FlushPendingSignalBatches();
        }

        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private sealed class StubLiveSessionSignalsWorkspace(Subject<LiveSignalBatch> signalBatches) : ILiveSessionSignalsWorkspace
    {
        public IObservable<LiveSignalBatch> SignalBatches { get; } = signalBatches;
        public LiveSessionPlotRanges PlotRanges { get; } = new(180, 5, 5);
        public IReadOnlyList<TrackPoint> TrackPoints { get; } = [];
        public TrackTimeRange? TrackTimelineContext { get; } = null;
        public SurfacePresentationState TravelSignalState { get; } = SurfacePresentationState.Ready;
        public SurfacePresentationState VelocitySignalState { get; } = SurfacePresentationState.Hidden;
        public SurfacePresentationState ImuSignalState { get; } = SurfacePresentationState.Hidden;
        public SurfacePresentationState PitchRollSignalState { get; } = SurfacePresentationState.Hidden;
        public SurfacePresentationState SpeedSignalState { get; } = SurfacePresentationState.Hidden;
        public SurfacePresentationState ElevationSignalState { get; } = SurfacePresentationState.Hidden;
        public SignalDisplayPreferences SignalDisplayPreferences { get; } = new();
        public SignalLayoutPreferences SignalLayoutPreferences { get; set; } = SignalLayoutPreferences.Default;
        public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();
        public SessionTimelineLinkViewModel Timeline { get; } = new();
    }
}
