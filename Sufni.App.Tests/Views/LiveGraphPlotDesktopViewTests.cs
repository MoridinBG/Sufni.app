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
        await WaitForUiRefreshAsync();

        var travelPlot = GetRenderedPlot(travelView!);
        var velocityPlot = GetRenderedPlot(velocityView!);
        var imuPlot = GetRenderedPlot(imuView!);

        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));
        Assert.All(velocityPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));
        Assert.Contains(imuPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => streamer.Data.CountTotal == 1);
        Assert.Equal(180, travelPlot.Plot.Axes.Left.Max);
        Assert.Equal(0, travelPlot.Plot.Axes.Left.Min);
        Assert.Equal(5, velocityPlot.Plot.Axes.Left.Max);
        Assert.Equal(-5, velocityPlot.Plot.Axes.Left.Min);
        Assert.Equal(5, imuPlot.Plot.Axes.Left.Max);
        Assert.Equal(0, imuPlot.Plot.Axes.Left.Min);

        batches.OnNext(LiveGraphBatch.Empty with { Revision = 2 });
        await WaitForUiRefreshAsync();

        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(0, streamer.Data.CountTotal));

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
        await WaitForUiRefreshAsync();

        var travelPlot = GetRenderedPlot(travelView!);
        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));

        host.Content = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        host.Content = view;
        await ViewTestHelpers.FlushDispatcherAsync();

        batches.OnNext(CreateBatch(revision: 2));
        await WaitForUiRefreshAsync();

        Assert.All(travelPlot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(6, streamer.Data.CountTotal));

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static AvaPlot GetRenderedPlot(Control view) =>
        Assert.Single(view.GetVisualDescendants().OfType<AvaPlot>());

    private static LiveGraphBatch CreateBatch(long revision)
    {
        return new LiveGraphBatch(
            Revision: revision,
            TravelTimes: [0.0, 0.01, 0.02],
            FrontTravel: [10.0, 11.0, 12.0],
            RearTravel: [9.0, 10.0, 11.0],
            VelocityTimes: [0.0, 0.01, 0.02],
            FrontVelocity: [1000.0, 1010.0, 1020.0],
            RearVelocity: [900.0, 910.0, 920.0],
            ImuTimes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>
            {
                [LiveImuLocation.Frame] = [0.02],
            },
            ImuMagnitudes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>
            {
                [LiveImuLocation.Frame] = [1.5],
            });
    }

    private static async Task WaitForUiRefreshAsync()
    {
        await Task.Delay(150);
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private sealed class StubLiveSessionGraphWorkspace(Subject<LiveGraphBatch> graphBatches) : ILiveSessionGraphWorkspace
    {
        public IObservable<LiveGraphBatch> GraphBatches { get; } = graphBatches;
        public LiveSessionPlotRanges PlotRanges { get; } = new(180, 5, 5);
        public SessionTimelineLinkViewModel Timeline { get; } = new();
    }
}
