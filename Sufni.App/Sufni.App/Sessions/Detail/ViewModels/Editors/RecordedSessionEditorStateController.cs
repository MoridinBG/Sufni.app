using System;
using System.Reactive.Linq;
using Sufni.App.Infrastructure;

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
        : this(
            legacyState,
            intents,
            pageCounts,
            Observable.Empty<AnalysisPreferences>())
    {
    }

    public RecordedSessionEditorStateController(
        IObservable<RecordedSessionEditorState> legacyState,
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<int> pageCounts,
        IObservable<AnalysisPreferences> analysisPreferenceReplays)
    {
        ArgumentNullException.ThrowIfNull(legacyState);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(pageCounts);
        ArgumentNullException.ThrowIfNull(analysisPreferenceReplays);

        var selectedPageIndex = CreateSelectedPageIndexState(intents, pageCounts);
        var analysisModes = CreateAnalysisModeState(intents, analysisPreferenceReplays);
        var derivedIntentState = selectedPageIndex
            .CombineLatest(
                analysisModes,
                static (pageIndex, modes) => new DerivedIntentState(pageIndex, modes));
        var replayingState = legacyState
            .CombineLatest(
                derivedIntentState,
                static (state, derived) => state with
                {
                    Intent = state.Intent with
                    {
                        SelectedPageIndex = derived.SelectedPageIndex,
                        SelectedTravelDistributionMode = derived.AnalysisModes.TravelDistributionMode,
                        SelectedBalanceDisplacementMode = derived.AnalysisModes.BalanceDisplacementMode,
                        SelectedBalanceSpeedMode = derived.AnalysisModes.BalanceSpeedMode,
                        SelectedVelocityAverageMode = derived.AnalysisModes.VelocityAverageMode,
                        SelectedSessionInsightsTargetProfile = derived.AnalysisModes.SessionInsightsTargetProfile,
                    },
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

    private static IObservable<AnalysisPreferences> CreateAnalysisModeState(
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<AnalysisPreferences> preferenceReplays)
    {
        var userUpdates = intents
            .Select(static intent => intent switch
            {
                RecordedSessionEditorIntent.SetTravelDistributionMode set =>
                    new Func<AnalysisPreferences, AnalysisPreferences>(
                        current => current with { TravelDistributionMode = set.Mode }),
                RecordedSessionEditorIntent.SetBalanceDisplacementMode set =>
                    new Func<AnalysisPreferences, AnalysisPreferences>(
                        current => current with { BalanceDisplacementMode = set.Mode }),
                RecordedSessionEditorIntent.SetBalanceSpeedMode set =>
                    new Func<AnalysisPreferences, AnalysisPreferences>(
                        current => current with { BalanceSpeedMode = set.Mode }),
                RecordedSessionEditorIntent.SetVelocityAverageMode set =>
                    new Func<AnalysisPreferences, AnalysisPreferences>(
                        current => current with { VelocityAverageMode = set.Mode }),
                RecordedSessionEditorIntent.SetSessionInsightsTargetProfile set =>
                    new Func<AnalysisPreferences, AnalysisPreferences>(
                        current => current with { SessionInsightsTargetProfile = set.Profile }),
                _ => null,
            })
            .Where(static update => update is not null)
            .Select(static update => update!);

        var replayUpdates = preferenceReplays
            .Select(static preferences => new Func<AnalysisPreferences, AnalysisPreferences>(_ => preferences));

        return userUpdates
            .Merge(replayUpdates)
            .StartWith(new Func<AnalysisPreferences, AnalysisPreferences>(static current => current))
            .Scan(SessionPreferences.Default.Analysis, static (current, update) => update(current))
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

    private sealed record DerivedIntentState(
        int SelectedPageIndex,
        AnalysisPreferences AnalysisModes);
}
