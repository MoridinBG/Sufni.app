using System;
using System.Reactive.Linq;
using Sufni.App.Infrastructure;
using Sufni.Telemetry;

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
            Observable.Empty<SessionPreferences>())
    {
    }

    public RecordedSessionEditorStateController(
        IObservable<RecordedSessionEditorState> legacyState,
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<int> pageCounts,
        IObservable<SessionPreferences> preferenceReplays)
    {
        ArgumentNullException.ThrowIfNull(legacyState);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(pageCounts);
        ArgumentNullException.ThrowIfNull(preferenceReplays);

        var selectedPageIndex = CreateSelectedPageIndexState(intents, pageCounts);
        var analysisRange = CreateAnalysisRangeState(intents);
        var preferenceIntent = CreatePreferenceIntentState(intents, preferenceReplays);
        var derivedIntentState = selectedPageIndex
            .CombineLatest(
                analysisRange,
                static (pageIndex, range) => new { pageIndex, range })
            .CombineLatest(
                preferenceIntent,
                static (current, preferences) => new DerivedIntentState(
                    current.pageIndex,
                    current.range,
                    preferences));
        var replayingState = legacyState
            .CombineLatest(
                derivedIntentState,
                static (state, derived) => state with
                {
                    Preferences = state.Preferences with
                    {
                        Analysis = derived.Preferences.Analysis,
                        SignalDisplay = derived.Preferences.SignalDisplay,
                        SignalLayout = derived.Preferences.SignalLayout,
                        Layout = derived.Preferences.Layout,
                    },
                    Intent = state.Intent with
                    {
                        SelectedPageIndex = derived.SelectedPageIndex,
                        AnalysisRange = ClampAnalysisRange(derived.AnalysisRange, state.TelemetryData),
                        SelectedTravelDistributionMode = derived.Preferences.Analysis.TravelDistributionMode,
                        SelectedBalanceDisplacementMode = derived.Preferences.Analysis.BalanceDisplacementMode,
                        SelectedBalanceSpeedMode = derived.Preferences.Analysis.BalanceSpeedMode,
                        SelectedVelocityAverageMode = derived.Preferences.Analysis.VelocityAverageMode,
                        SelectedSessionInsightsTargetProfile = derived.Preferences.Analysis.SessionInsightsTargetProfile,
                        SignalDisplayPreferences = derived.Preferences.SignalDisplay,
                        SignalLayoutPreferences = derived.Preferences.SignalLayout,
                        LayoutPreferences = derived.Preferences.Layout,
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

    private static IObservable<TelemetryTimeRange?> CreateAnalysisRangeState(
        IObservable<RecordedSessionEditorIntent> intents)
    {
        var updates = intents
            .Select(static intent => intent switch
            {
                RecordedSessionEditorIntent.SetAnalysisRange set =>
                    new Func<TelemetryTimeRange?, TelemetryTimeRange?>(_ => set.Range),
                RecordedSessionEditorIntent.ClearAnalysisRange =>
                    new Func<TelemetryTimeRange?, TelemetryTimeRange?>(_ => null),
                _ => null,
            })
            .Where(static update => update is not null)
            .Select(static update => update!);

        return updates
            .StartWith(new Func<TelemetryTimeRange?, TelemetryTimeRange?>(static current => current))
            .Scan((TelemetryTimeRange?)null, static (current, update) => update(current))
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount();
    }

    private static IObservable<SessionPreferences> CreatePreferenceIntentState(
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<SessionPreferences> preferenceReplays)
    {
        var userUpdates = intents
            .Select(static intent => intent switch
            {
                RecordedSessionEditorIntent.SetTravelDistributionMode set =>
                    new Func<SessionPreferences, SessionPreferences>(
                        current => current with
                        {
                            Analysis = current.Analysis with { TravelDistributionMode = set.Mode },
                        }),
                RecordedSessionEditorIntent.SetBalanceDisplacementMode set =>
                    new Func<SessionPreferences, SessionPreferences>(
                        current => current with
                        {
                            Analysis = current.Analysis with { BalanceDisplacementMode = set.Mode },
                        }),
                RecordedSessionEditorIntent.SetBalanceSpeedMode set =>
                    new Func<SessionPreferences, SessionPreferences>(
                        current => current with
                        {
                            Analysis = current.Analysis with { BalanceSpeedMode = set.Mode },
                        }),
                RecordedSessionEditorIntent.SetVelocityAverageMode set =>
                    new Func<SessionPreferences, SessionPreferences>(
                        current => current with
                        {
                            Analysis = current.Analysis with { VelocityAverageMode = set.Mode },
                        }),
                RecordedSessionEditorIntent.SetSessionInsightsTargetProfile set =>
                    new Func<SessionPreferences, SessionPreferences>(
                        current => current with
                        {
                            Analysis = current.Analysis with { SessionInsightsTargetProfile = set.Profile },
                        }),
                RecordedSessionEditorIntent.SetSignalDisplayPreferences set =>
                    new Func<SessionPreferences, SessionPreferences>(
                        current => current with { SignalDisplay = set.Preferences }),
                RecordedSessionEditorIntent.SetSignalLayoutPreferences set =>
                    new Func<SessionPreferences, SessionPreferences>(
                        current => current with { SignalLayout = set.Preferences }),
                RecordedSessionEditorIntent.SetLayoutPreferences set =>
                    new Func<SessionPreferences, SessionPreferences>(
                        current => current with { Layout = set.Preferences }),
                _ => null,
            })
            .Where(static update => update is not null)
            .Select(static update => update!);

        var replayUpdates = preferenceReplays
            .Select(static preferences => new Func<SessionPreferences, SessionPreferences>(_ => preferences));

        return userUpdates
            .Merge(replayUpdates)
            .StartWith(new Func<SessionPreferences, SessionPreferences>(static current => current))
            .Scan(SessionPreferences.Default, static (current, update) => update(current))
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

    private static TelemetryTimeRange? ClampAnalysisRange(TelemetryTimeRange? range, TelemetryData? telemetryData)
    {
        if (!range.HasValue || telemetryData is null)
        {
            return null;
        }

        return TelemetryTimeRange.TryCreateClamped(
            range.Value.StartSeconds,
            range.Value.EndSeconds,
            telemetryData.Metadata.Duration,
            out var clampedRange)
            ? clampedRange
            : null;
    }

    private sealed record PageSelectionUpdate(int? PageIndex, int? PageCount);

    private sealed record PageSelectionState(int SelectedPageIndex, int PageCount);

    private sealed record DerivedIntentState(
        int SelectedPageIndex,
        TelemetryTimeRange? AnalysisRange,
        SessionPreferences Preferences);
}
