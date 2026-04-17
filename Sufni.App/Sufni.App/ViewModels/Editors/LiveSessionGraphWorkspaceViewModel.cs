using System;
using System.Reactive.Linq;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.ViewModels;

namespace Sufni.App.ViewModels.Editors;

public sealed class LiveSessionGraphWorkspaceViewModel : ViewModelBase, ILiveSessionGraphWorkspace
{
    public IObservable<LiveGraphBatch> GraphBatches { get; private set; } = Observable.Empty<LiveGraphBatch>();
    public LiveSessionPlotRanges PlotRanges { get; }
    public SessionTimelineLinkViewModel Timeline { get; }

    public LiveSessionGraphWorkspaceViewModel()
        : this(new SessionTimelineLinkViewModel(), LiveSessionPlotRanges.Default)
    {
    }

    public LiveSessionGraphWorkspaceViewModel(
        SessionTimelineLinkViewModel timeline,
        LiveSessionPlotRanges plotRanges)
    {
        Timeline = timeline;
        PlotRanges = plotRanges;
    }

    public void Attach(IObservable<LiveGraphBatch> graphBatches)
    {
        GraphBatches = graphBatches;
    }
}