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
using Sufni.App.Presentation;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Views;

public class LiveGraphPlotDesktopViewTests
{
    [AvaloniaFact]
    public async Task LiveSessionGraphDesktopView_WiresLivePlotViews_AndAppendsGraphBatches()
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

        var travelView = view.FindControl<LiveTravelPlotDesktopView>("TravelPlot");
        var velocityView = view.FindControl<LiveVelocityPlotDesktopView>("VelocityPlot");
        var imuView = view.FindControl<LiveImuPlotDesktopView>("ImuPlot");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);

        batches.OnNext(CreateBatch(revision: 1));
        await FlushGraphBatchesAsync(travelView!, velocityView!, imuView!);

        var travelPlot = GetRenderedPlot(travelView!);
        var velocityPlot = GetRenderedPlot(velocityView!);
        var imuPlot = GetRenderedPlot(imuView!);

        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));
        Assert.All(velocityPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));
        Assert.Contains(imuPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => streamer.Data.CountTotal == 1);
        // Live plots auto-size around the largest value seen so far (with 10% headroom),
        // falling back to a per-metric floor when running max is below it.
        Assert.Equal(13.2, travelPlot.Plot.Axes.Left.Max, precision: 4);
        Assert.Equal(0, travelPlot.Plot.Axes.Left.Min);
        Assert.Equal(1.122, velocityPlot.Plot.Axes.Left.Max, precision: 4);
        Assert.Equal(-1.122, velocityPlot.Plot.Axes.Left.Min, precision: 4);
        Assert.Equal(1.65, imuPlot.Plot.Axes.Left.Max, precision: 4);
        Assert.Equal(0, imuPlot.Plot.Axes.Left.Min);

        batches.OnNext(LiveGraphBatch.Empty with { Revision = 2 });
        await FlushGraphBatchesAsync(travelView!, velocityView!, imuView!);

        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(0, streamer.Data.CountTotal));
        // After a reset batch the running max clears, so axes shrink back to the floor.
        Assert.Equal(5, travelPlot.Plot.Axes.Left.Max);
        Assert.Equal(0.5, velocityPlot.Plot.Axes.Left.Max);
        Assert.Equal(-0.5, velocityPlot.Plot.Axes.Left.Min);
        Assert.Equal(1, imuPlot.Plot.Axes.Left.Max);

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

        var travelView = view.FindControl<LiveTravelPlotDesktopView>("TravelPlot");
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

        var travelView = view.FindControl<LiveTravelPlotDesktopView>("TravelPlot");
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

        var grid = view.FindControl<Grid>("GraphGrid");
        var travelView = view.FindControl<LiveTravelPlotDesktopView>("TravelPlot");
        var velocityView = view.FindControl<LiveVelocityPlotDesktopView>("VelocityPlot");
        var imuView = view.FindControl<LiveImuPlotDesktopView>("ImuPlot");

        Assert.NotNull(grid);
        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.Equal(0, grid!.RowDefinitions[0].Height.Value);
        Assert.Equal(GridUnitType.Pixel, grid.RowDefinitions[0].Height.GridUnitType);
        Assert.NotEqual(0, grid.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Star, grid.RowDefinitions[2].Height.GridUnitType);

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

        var grid = view.FindControl<Grid>("GraphGrid");
        var imuView = view.FindControl<LiveImuPlotDesktopView>("ImuPlot");

        Assert.NotNull(grid);
        Assert.NotNull(imuView);
        Assert.Equal(0, grid!.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Pixel, grid.RowDefinitions[2].Height.GridUnitType);

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

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
            ImuMagnitudes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>
            {
                [LiveImuLocation.Frame] = [1.5],
            });
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
        bool hasImuSection = true) : ILiveSessionGraphWorkspace
    {
        public IObservable<LiveGraphBatch> GraphBatches { get; } = graphBatches;
        public LiveSessionPlotRanges PlotRanges { get; } = new(180, 5, 5);
        public SurfacePresentationState TravelGraphState { get; } = hasTravelSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState ImuGraphState { get; } = hasImuSection
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SessionTimelineLinkViewModel Timeline { get; } = new();
    }
}
