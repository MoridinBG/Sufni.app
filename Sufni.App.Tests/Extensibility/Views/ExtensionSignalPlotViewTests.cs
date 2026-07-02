using System.Linq;
using Avalonia.Headless.XUnit;
using ScottPlot.Plottables;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.Extensibility.Views;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Extensibility.Views;

[Collection("Ui")]
public class ExtensionSignalPlotViewTests
{
    [AvaloniaFact]
    public async Task BindingNeutralViewModel_PropagatesTimelineAndShowAirtimeToInheritedProperties()
    {
        var timeline = new SessionTimelineLinkViewModel();
        var viewModel = CreateViewModel(airtimeSpans: [new RecordedSessionSignalSpan(1, 2)]);
        viewModel.Timeline = timeline;
        var view = new ExtensionSignalPlotView { DataContext = viewModel };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Same(timeline, view.Timeline);
        Assert.False(view.ShowAirtime);

        viewModel.ShowAirtime = true;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(view.ShowAirtime);

        var nextTimeline = new SessionTimelineLinkViewModel();
        viewModel.Timeline = nextTimeline;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Same(nextTimeline, view.Timeline);
    }

    [AvaloniaFact]
    public async Task RendersNeutralSeries_OntoTheHostedPlot()
    {
        var view = new ExtensionSignalPlotView { DataContext = CreateViewModel() };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Single(plot.Plot.GetPlottables().OfType<Scatter>());
    }

    private static RecordedSessionSignalPlotViewModel CreateViewModel(
        IReadOnlyList<RecordedSessionSignalSpan>? airtimeSpans = null) =>
        new(
            [new RecordedSessionSignalSeries(
                RecordedSessionSignalSeriesRole.FrontSuspension, "Front", "mm", [0, 1, 2], [10, 20, 15])],
            invertValueAxis: true,
            durationSeconds: 10,
            emptyMessage: "No matched data",
            airtimeSpans ?? []);
}
