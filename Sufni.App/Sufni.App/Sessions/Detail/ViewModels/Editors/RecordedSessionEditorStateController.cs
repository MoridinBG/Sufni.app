using System;
using System.Reactive.Linq;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Presentation;
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
        : this(
            legacyState,
            intents,
            pageCounts,
            preferenceReplays,
            Observable.Empty<SessionScreenPresentationState>(),
            Observable.Empty<SessionOperationPresentationState>())
    {
    }

    public RecordedSessionEditorStateController(
        IObservable<RecordedSessionEditorState> legacyState,
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<int> pageCounts,
        IObservable<SessionPreferences> preferenceReplays,
        IObservable<SessionScreenPresentationState> screenStates,
        IObservable<SessionOperationPresentationState> operationStates)
        : this(
            legacyState,
            intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates,
            Observable.Empty<SurfacePresentationState>(),
            Observable.Empty<SurfacePresentationState>(),
            Observable.Empty<double?>(),
            Observable.Empty<string?>())
    {
    }

    public RecordedSessionEditorStateController(
        IObservable<RecordedSessionEditorState> legacyState,
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<int> pageCounts,
        IObservable<SessionPreferences> preferenceReplays,
        IObservable<SessionScreenPresentationState> screenStates,
        IObservable<SessionOperationPresentationState> operationStates,
        IObservable<SurfacePresentationState> mapStates,
        IObservable<SurfacePresentationState> mediaPaneStates,
        IObservable<double?> mediaColumnWidths,
        IObservable<string?> mediaUrls)
        : this(
            legacyState,
            intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates,
            mapStates,
            mediaPaneStates,
            mediaColumnWidths,
            mediaUrls,
            Observable.Empty<RecordedAnalysisPresentationState>())
    {
    }

    public RecordedSessionEditorStateController(
        IObservable<RecordedSessionEditorState> legacyState,
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<int> pageCounts,
        IObservable<SessionPreferences> preferenceReplays,
        IObservable<SessionScreenPresentationState> screenStates,
        IObservable<SessionOperationPresentationState> operationStates,
        IObservable<SurfacePresentationState> mapStates,
        IObservable<SurfacePresentationState> mediaPaneStates,
        IObservable<double?> mediaColumnWidths,
        IObservable<string?> mediaUrls,
        IObservable<RecordedAnalysisPresentationState> analysisPresentationStates)
        : this(
            legacyState,
            intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates,
            mapStates,
            mediaPaneStates,
            mediaColumnWidths,
            mediaUrls,
            analysisPresentationStates,
            Observable.Empty<SessionDampingPercentages>(),
            Observable.Empty<DampingSpeedCutoffs>(),
            Observable.Empty<bool>(),
            Observable.Empty<SessionInsightsResult>())
    {
    }

    public RecordedSessionEditorStateController(
        IObservable<RecordedSessionEditorState> legacyState,
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<int> pageCounts,
        IObservable<SessionPreferences> preferenceReplays,
        IObservable<SessionScreenPresentationState> screenStates,
        IObservable<SessionOperationPresentationState> operationStates,
        IObservable<SurfacePresentationState> mapStates,
        IObservable<SurfacePresentationState> mediaPaneStates,
        IObservable<double?> mediaColumnWidths,
        IObservable<string?> mediaUrls,
        IObservable<RecordedAnalysisPresentationState> analysisPresentationStates,
        IObservable<SessionDampingPercentages> dampingPercentages,
        IObservable<DampingSpeedCutoffs> plotDampingSpeedCutoffs,
        IObservable<bool> canEditDampingSpeedCutoffs,
        IObservable<SessionInsightsResult> sessionInsights)
        : this(
            legacyState,
            intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates,
            mapStates,
            mediaPaneStates,
            mediaColumnWidths,
            mediaUrls,
            analysisPresentationStates,
            dampingPercentages,
            plotDampingSpeedCutoffs,
            canEditDampingSpeedCutoffs,
            sessionInsights,
            Observable.Empty<RecordedSignalPresentationState>())
    {
    }

    public RecordedSessionEditorStateController(
        IObservable<RecordedSessionEditorState> legacyState,
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<int> pageCounts,
        IObservable<SessionPreferences> preferenceReplays,
        IObservable<SessionScreenPresentationState> screenStates,
        IObservable<SessionOperationPresentationState> operationStates,
        IObservable<SurfacePresentationState> mapStates,
        IObservable<SurfacePresentationState> mediaPaneStates,
        IObservable<double?> mediaColumnWidths,
        IObservable<string?> mediaUrls,
        IObservable<RecordedAnalysisPresentationState> analysisPresentationStates,
        IObservable<SessionDampingPercentages> dampingPercentages,
        IObservable<DampingSpeedCutoffs> plotDampingSpeedCutoffs,
        IObservable<bool> canEditDampingSpeedCutoffs,
        IObservable<SessionInsightsResult> sessionInsights,
        IObservable<RecordedSignalPresentationState> signalPresentationStates)
        : this(
            legacyState,
            intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates,
            mapStates,
            mediaPaneStates,
            mediaColumnWidths,
            mediaUrls,
            analysisPresentationStates,
            dampingPercentages,
            plotDampingSpeedCutoffs,
            canEditDampingSpeedCutoffs,
            sessionInsights,
            signalPresentationStates,
            Observable.Empty<AnalysisSelectionState>())
    {
    }

    public RecordedSessionEditorStateController(
        IObservable<RecordedSessionEditorState> legacyState,
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<int> pageCounts,
        IObservable<SessionPreferences> preferenceReplays,
        IObservable<SessionScreenPresentationState> screenStates,
        IObservable<SessionOperationPresentationState> operationStates,
        IObservable<SurfacePresentationState> mapStates,
        IObservable<SurfacePresentationState> mediaPaneStates,
        IObservable<double?> mediaColumnWidths,
        IObservable<string?> mediaUrls,
        IObservable<RecordedAnalysisPresentationState> analysisPresentationStates,
        IObservable<SessionDampingPercentages> dampingPercentages,
        IObservable<DampingSpeedCutoffs> plotDampingSpeedCutoffs,
        IObservable<bool> canEditDampingSpeedCutoffs,
        IObservable<SessionInsightsResult> sessionInsights,
        IObservable<RecordedSignalPresentationState> signalPresentationStates,
        IObservable<AnalysisSelectionState> analysisSelectionStates)
    {
        ArgumentNullException.ThrowIfNull(legacyState);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(pageCounts);
        ArgumentNullException.ThrowIfNull(preferenceReplays);
        ArgumentNullException.ThrowIfNull(screenStates);
        ArgumentNullException.ThrowIfNull(operationStates);
        ArgumentNullException.ThrowIfNull(mapStates);
        ArgumentNullException.ThrowIfNull(mediaPaneStates);
        ArgumentNullException.ThrowIfNull(mediaColumnWidths);
        ArgumentNullException.ThrowIfNull(mediaUrls);
        ArgumentNullException.ThrowIfNull(analysisPresentationStates);
        ArgumentNullException.ThrowIfNull(dampingPercentages);
        ArgumentNullException.ThrowIfNull(plotDampingSpeedCutoffs);
        ArgumentNullException.ThrowIfNull(canEditDampingSpeedCutoffs);
        ArgumentNullException.ThrowIfNull(sessionInsights);
        ArgumentNullException.ThrowIfNull(signalPresentationStates);
        ArgumentNullException.ThrowIfNull(analysisSelectionStates);

        var selectedPageIndex = CreateSelectedPageIndexState(intents, pageCounts);
        var analysisRange = CreateAnalysisRangeState(intents);
        var dampingSpeedCutoffs = CreateDampingSpeedCutoffsState(intents);
        var preferenceIntent = CreatePreferenceIntentState(intents, preferenceReplays);
        var screenState = CreateInputState(screenStates, SessionScreenPresentationState.Ready);
        var operationState = CreateInputState(operationStates, SessionOperationPresentationState.Hidden);
        var mapState = CreateInputState(mapStates, SurfacePresentationState.Hidden);
        var mediaPaneState = CreateInputState(mediaPaneStates, SurfacePresentationState.Hidden);
        var mediaColumnWidth = CreateInputState(mediaColumnWidths, (double?)null);
        var mediaUrl = CreateInputState(mediaUrls, (string?)null);
        var analysisPresentation = CreateInputState(
            analysisPresentationStates,
            CreateHiddenAnalysisPresentationState());
        var dampingPercentageState = CreateInputState(
            dampingPercentages,
            SessionDampingPercentages.Empty);
        var plotDampingSpeedCutoffState = CreateInputState(
            plotDampingSpeedCutoffs,
            DampingSpeedCutoffs.Default);
        var canEditDampingSpeedCutoffState = CreateInputState(
            canEditDampingSpeedCutoffs,
            false);
        var sessionInsightsState = CreateInputState(
            sessionInsights,
            SessionInsightsResult.Hidden);
        var signalPresentationState = CreateInputState(
            signalPresentationStates,
            CreateHiddenSignalPresentationState());
        var analysisSelectionState = CreateInputState(
            analysisSelectionStates,
            new AnalysisSelectionState(ActiveFront: null, ActiveRear: null, HighlightRanges: []));
        var derivedIntentState = selectedPageIndex
            .CombineLatest(
                analysisRange,
                static (pageIndex, range) => new { pageIndex, range })
            .CombineLatest(
                dampingSpeedCutoffs,
                static (current, cutoffs) => new { current.pageIndex, current.range, cutoffs })
            .CombineLatest(
                preferenceIntent,
                static (current, preferences) => new DerivedIntentState(
                    current.pageIndex,
                    current.range,
                    current.cutoffs,
                    preferences));
        var derivedPresentationState = screenState
            .CombineLatest(
                operationState,
                static (screen, operation) => new DerivedPresentationState(screen, operation));
        var derivedMediaState = mapState
            .CombineLatest(
                mediaPaneState,
                static (map, pane) => new { map, pane })
            .CombineLatest(
                mediaColumnWidth,
                static (current, width) => new { current.map, current.pane, width })
            .CombineLatest(
                mediaUrl,
                static (current, url) => new DerivedMediaPresentationState(
                    current.map,
                    current.pane,
                    current.width,
                    url));
        var replayingState = legacyState
            .CombineLatest(
                derivedIntentState,
                static (state, derived) => new { state, derived })
            .CombineLatest(
                derivedPresentationState,
                static (current, presentation) => new { current.state, current.derived, presentation })
            .CombineLatest(
                derivedMediaState,
                static (current, media) => new { current.state, current.derived, current.presentation, media })
            .CombineLatest(
                analysisPresentation,
                static (current, analysis) => new { current.state, current.derived, current.presentation, current.media, analysis })
            .CombineLatest(
                dampingPercentageState,
                static (current, percentages) => new { current.state, current.derived, current.presentation, current.media, current.analysis, percentages })
            .CombineLatest(
                plotDampingSpeedCutoffState,
                static (current, plotCutoffs) => new { current.state, current.derived, current.presentation, current.media, current.analysis, current.percentages, plotCutoffs })
            .CombineLatest(
                canEditDampingSpeedCutoffState,
                static (current, canEditCutoffs) => new { current.state, current.derived, current.presentation, current.media, current.analysis, current.percentages, current.plotCutoffs, canEditCutoffs })
            .CombineLatest(
                sessionInsightsState,
                static (current, insights) => new { current.state, current.derived, current.presentation, current.media, current.analysis, current.percentages, current.plotCutoffs, current.canEditCutoffs, insights })
            .CombineLatest(
                signalPresentationState,
                static (current, signals) => new { current.state, current.derived, current.presentation, current.media, current.analysis, current.percentages, current.plotCutoffs, current.canEditCutoffs, current.insights, signals })
            .CombineLatest(
                analysisSelectionState,
                static (current, analysisSelection) => current.state with
                {
                    Preferences = current.state.Preferences with
                    {
                        Analysis = current.derived.Preferences.Analysis,
                        SignalDisplay = current.derived.Preferences.SignalDisplay,
                        SignalLayout = current.derived.Preferences.SignalLayout,
                        Layout = current.derived.Preferences.Layout,
                    },
                    Intent = current.state.Intent with
                    {
                        SelectedPageIndex = current.derived.SelectedPageIndex,
                        AnalysisRange = ClampAnalysisRange(current.derived.AnalysisRange, current.state.TelemetryData),
                        SelectedTravelDistributionMode = current.derived.Preferences.Analysis.TravelDistributionMode,
                        SelectedBalanceDisplacementMode = current.derived.Preferences.Analysis.BalanceDisplacementMode,
                        SelectedBalanceSpeedMode = current.derived.Preferences.Analysis.BalanceSpeedMode,
                        SelectedVelocityAverageMode = current.derived.Preferences.Analysis.VelocityAverageMode,
                        SelectedSessionInsightsTargetProfile = current.derived.Preferences.Analysis.SessionInsightsTargetProfile,
                        DampingSpeedCutoffs = current.derived.DampingSpeedCutoffs,
                        SignalDisplayPreferences = current.derived.Preferences.SignalDisplay,
                        SignalLayoutPreferences = current.derived.Preferences.SignalLayout,
                        LayoutPreferences = current.derived.Preferences.Layout,
                    },
                    Presentation = current.state.Presentation with
                    {
                        MapState = current.media.MapState,
                        MediaPaneState = current.media.MediaPaneState,
                        MediaColumnWidth = current.media.MediaColumnWidth,
                        MediaUrl = current.media.MediaUrl,
                        Signals = current.signals,
                        Analysis = current.analysis,
                        DampingPercentages = current.percentages,
                        PlotDampingSpeedCutoffs = current.plotCutoffs,
                        CanEditDampingSpeedCutoffs = current.canEditCutoffs,
                        SessionInsights = current.insights,
                        ScreenState = current.presentation.ScreenState,
                        OperationState = current.presentation.OperationState,
                    },
                    AnalysisSelection = analysisSelection,
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

    private static RecordedAnalysisPresentationState CreateHiddenAnalysisPresentationState()
    {
        return new RecordedAnalysisPresentationState(
            FrontAnalysis: SurfacePresentationState.Hidden,
            RearAnalysis: SurfacePresentationState.Hidden,
            CompressionBalance: SurfacePresentationState.Hidden,
            ReboundBalance: SurfacePresentationState.Hidden,
            FrontForkVibration: SurfacePresentationState.Hidden,
            FrontFrameVibration: SurfacePresentationState.Hidden,
            RearForkVibration: SurfacePresentationState.Hidden,
            RearFrameVibration: SurfacePresentationState.Hidden);
    }

    private static RecordedSignalPresentationState CreateHiddenSignalPresentationState()
    {
        return new RecordedSignalPresentationState(
            Travel: SurfacePresentationState.Hidden,
            Velocity: SurfacePresentationState.Hidden,
            Imu: SurfacePresentationState.Hidden,
            PitchRoll: SurfacePresentationState.Hidden,
            Speed: SurfacePresentationState.Hidden,
            Elevation: SurfacePresentationState.Hidden,
            ShowAirtime: false,
            ShowVelocityAirtime: false,
            ShowImuAirtime: false,
            ShowPitchRollAirtime: false,
            ShowSpeedAirtime: false,
            ShowElevationAirtime: false,
            ShowAnalysisSelection: false,
            ShowVelocityAnalysisSelection: false,
            ShowImuAnalysisSelection: false,
            ShowPitchRollAnalysisSelection: false,
            ShowSpeedAnalysisSelection: false,
            ShowElevationAnalysisSelection: false,
            TravelHeaderActions: [],
            VelocityHeaderActions: [],
            ImuHeaderActions: [],
            PitchRollHeaderActions: [],
            SpeedHeaderActions: [],
            ElevationHeaderActions: []);
    }

    private static IObservable<T> CreateInputState<T>(IObservable<T> updates, T initialValue)
    {
        return updates
            .StartWith(initialValue)
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

    private static IObservable<DampingSpeedCutoffs> CreateDampingSpeedCutoffsState(
        IObservable<RecordedSessionEditorIntent> intents)
    {
        return intents
            .OfType<RecordedSessionEditorIntent.SetDampingSpeedCutoffs>()
            .Select(static intent => new Func<DampingSpeedCutoffs, DampingSpeedCutoffs>(_ => intent.Cutoffs))
            .StartWith(new Func<DampingSpeedCutoffs, DampingSpeedCutoffs>(static current => current))
            .Scan(DampingSpeedCutoffs.Default, static (current, update) => update(current))
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
        DampingSpeedCutoffs DampingSpeedCutoffs,
        SessionPreferences Preferences);

    private sealed record DerivedPresentationState(
        SessionScreenPresentationState ScreenState,
        SessionOperationPresentationState OperationState);

    private sealed record DerivedMediaPresentationState(
        SurfacePresentationState MapState,
        SurfacePresentationState MediaPaneState,
        double? MediaColumnWidth,
        string? MediaUrl);
}
