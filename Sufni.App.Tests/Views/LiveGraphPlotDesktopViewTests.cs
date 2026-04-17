using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Items;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Views;

public class LiveGraphPlotDesktopViewTests
{
    [AvaloniaFact]
    public async Task LiveSessionGraphDesktopView_WiresLivePlotViews_AndAppendsGraphBatches()
    {
        EnsurePlotViewStyle();

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
        await FlushAsync();

        var travelView = view.FindControl<LiveTravelPlotDesktopView>("TravelPlot");
        var velocityView = view.FindControl<LiveVelocityPlotDesktopView>("VelocityPlot");
        var imuView = view.FindControl<LiveImuPlotDesktopView>("ImuPlot");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);

        batches.OnNext(CreateBatch(revision: 1));
        await WaitForUiRefreshAsync();

        Assert.All(travelView!.Plot!.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));
        Assert.All(velocityView!.Plot!.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(3, streamer.Data.CountTotal));
        Assert.Contains(imuView!.Plot!.Plot.PlottableList.OfType<DataStreamer>(), streamer => streamer.Data.CountTotal == 1);
        Assert.Equal(180, travelView.Plot.Plot.Axes.Left.Max);
        Assert.Equal(0, travelView.Plot.Plot.Axes.Left.Min);
        Assert.Equal(5, velocityView.Plot.Plot.Axes.Left.Max);
        Assert.Equal(-5, velocityView.Plot.Plot.Axes.Left.Min);
        Assert.Equal(5, imuView.Plot.Plot.Axes.Left.Max);
        Assert.Equal(0, imuView.Plot.Plot.Axes.Left.Min);

        batches.OnNext(LiveGraphBatch.Empty with { Revision = 2 });
        await WaitForUiRefreshAsync();

        Assert.All(travelView.Plot.Plot.PlottableList.OfType<DataStreamer>(), streamer => Assert.Equal(0, streamer.Data.CountTotal));

        host.Close();
        await FlushAsync();
    }

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

    private static async Task FlushAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static async Task WaitForUiRefreshAsync()
    {
        await Task.Delay(150);
        await FlushAsync();
    }

    private static void EnsurePlotViewStyle()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");
        var source = new Uri("avares://Sufni.App/Views/Plots/SufniPlotView.axaml");

        if (application.Styles.OfType<StyleInclude>().Any(style => style.Source?.AbsoluteUri == source.AbsoluteUri))
        {
            return;
        }

        application.Styles.Add(new StyleInclude(new Uri("avares://Sufni.App/"))
        {
            Source = source
        });
    }

    private sealed class StubLiveSessionGraphWorkspace(Subject<LiveGraphBatch> graphBatches) : ILiveSessionGraphWorkspace
    {
        public IObservable<LiveGraphBatch> GraphBatches { get; } = graphBatches;
        public LiveSessionPlotRanges PlotRanges { get; } = new(180, 5, 5);
        public SessionTimelineLinkViewModel Timeline { get; } = new();
    }
}
