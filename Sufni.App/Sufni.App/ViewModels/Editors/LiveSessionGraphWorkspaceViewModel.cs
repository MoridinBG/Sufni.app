using System;
using System.Reactive.Linq;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.ViewModels;

namespace Sufni.App.ViewModels.Editors;

public sealed class LiveSessionGraphWorkspaceViewModel : ViewModelBase, ILiveSessionGraphWorkspace
{
    public IObservable<LiveGraphBatch> GraphBatches { get; }
    public LiveSessionPlotRanges PlotRanges { get; }
    public SessionTimelineLinkViewModel Timeline { get; }

    public LiveSessionGraphWorkspaceViewModel()
        : this(new SessionTimelineLinkViewModel(), LiveSessionPlotRanges.Default, Observable.Empty<LiveGraphBatch>())
    {
    }

    public LiveSessionGraphWorkspaceViewModel(
        SessionTimelineLinkViewModel timeline,
        LiveSessionPlotRanges plotRanges,
        IObservable<LiveGraphBatch> graphBatches)
    {
        Timeline = timeline;
        PlotRanges = plotRanges;
        GraphBatches = graphBatches;
    }
}