using System;
using System.Reactive.Linq;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class RecordedSessionEditorStateController : IDisposable
{
    private readonly IDisposable connection;
    private bool disposed;

    public RecordedSessionEditorStateController(IObservable<RecordedSessionEditorState> state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var replayingState = state
            .DistinctUntilChanged()
            .Replay(1);

        State = replayingState.AsObservable();
        connection = replayingState.Connect();
    }

    public RecordedSessionEditorStateController(
        IObservable<RecordedSessionEditorState> legacyState,
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<int> pageCounts)
    {
        ArgumentNullException.ThrowIfNull(legacyState);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(pageCounts);

        var selectedPageIndex = CreateSelectedPageIndexState(intents, pageCounts);
        var replayingState = legacyState
            .CombineLatest(
                selectedPageIndex,
                static (state, pageIndex) => state with
                {
                    Intent = state.Intent with { SelectedPageIndex = pageIndex },
                })
            .DistinctUntilChanged()
            .Replay(1);

        State = replayingState.AsObservable();
        connection = replayingState.Connect();
    }

    public IObservable<RecordedSessionEditorState> State { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        connection.Dispose();
    }

    private static IObservable<int> CreateSelectedPageIndexState(
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<int> pageCounts)
    {
        var selectedPageRequests = intents
            .OfType<RecordedSessionEditorIntent.SelectPageIndex>()
            .Select(static intent => new PageSelectionUpdate(PageIndex: Math.Max(0, intent.PageIndex), PageCount: null));

        var pageCountClamps = pageCounts
            .StartWith(0)
            .Select(static count => new PageSelectionUpdate(PageIndex: null, PageCount: count));

        return selectedPageRequests
            .Merge(pageCountClamps)
            .Scan(
                new PageSelectionState(SelectedPageIndex: 0, PageCount: 0),
                static (current, update) =>
                {
                    var pageCount = update.PageCount ?? current.PageCount;
                    var selectedPageIndex = update.PageIndex ?? current.SelectedPageIndex;
                    return new PageSelectionState(
                        ClampSelectedPageIndex(selectedPageIndex, pageCount),
                        pageCount);
                })
            .Select(static state => state.SelectedPageIndex)
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount();
    }

    private static int ClampSelectedPageIndex(int pageIndex, int pageCount)
    {
        if (pageCount <= 0)
        {
            return 0;
        }

        if (pageIndex < 0)
        {
            return 0;
        }

        return pageIndex >= pageCount
            ? pageCount - 1
            : pageIndex;
    }

    private sealed record PageSelectionUpdate(int? PageIndex, int? PageCount);

    private sealed record PageSelectionState(int SelectedPageIndex, int PageCount);
}
