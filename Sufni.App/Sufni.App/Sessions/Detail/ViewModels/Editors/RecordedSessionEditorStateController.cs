using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Analysis.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Store;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed record RecordedSessionEditorStateInputs(
    IObservable<RecordedSessionEditorIntent> Intents,
    IObservable<int> PageCounts,
    IObservable<SessionPreferences> PreferenceReplays,
    IObservable<RecordedSessionLoadPresentation> LoadPresentations,
    IObservable<SessionOperationPresentationState> OperationStates,
    IObservable<SurfacePresentationState> MediaPaneStates,
    IObservable<string?> MediaUrls,
    IObservable<SessionDampingPercentages> DampingPercentages,
    IObservable<DampingSpeedCutoffs> PlotDampingSpeedCutoffs,
    IObservable<bool> CanEditDampingSpeedCutoffs,
    IObservable<SessionInsightsResult> SessionInsights,
    IObservable<AnalysisSelectionState> AnalysisSelections,
    IObservable<IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>> SignalPlotContextMenuActions,
    IObservable<RecordedSessionDomainSnapshot> DomainStates);

internal sealed class RecordedSessionEditorStateController : IDisposable
{
    private readonly IDisposable connection;
    private bool disposed;

    public RecordedSessionEditorStateController(RecordedSessionEditorStateInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputs.Intents);
        ArgumentNullException.ThrowIfNull(inputs.PageCounts);
        ArgumentNullException.ThrowIfNull(inputs.PreferenceReplays);
        ArgumentNullException.ThrowIfNull(inputs.LoadPresentations);
        ArgumentNullException.ThrowIfNull(inputs.OperationStates);
        ArgumentNullException.ThrowIfNull(inputs.MediaPaneStates);
        ArgumentNullException.ThrowIfNull(inputs.MediaUrls);
        ArgumentNullException.ThrowIfNull(inputs.DampingPercentages);
        ArgumentNullException.ThrowIfNull(inputs.PlotDampingSpeedCutoffs);
        ArgumentNullException.ThrowIfNull(inputs.CanEditDampingSpeedCutoffs);
        ArgumentNullException.ThrowIfNull(inputs.SessionInsights);
        ArgumentNullException.ThrowIfNull(inputs.AnalysisSelections);
        ArgumentNullException.ThrowIfNull(inputs.SignalPlotContextMenuActions);
        ArgumentNullException.ThrowIfNull(inputs.DomainStates);

        var selectedPageIndex = CreateSelectedPageIndexState(inputs.Intents, inputs.PageCounts);
        var loadPresentationState = CreateInputState(
            inputs.LoadPresentations,
            new RecordedSessionLoadPresentation.Empty());
        var loadedDataState = CreateInputState(
            loadPresentationState.Select(CreateLoadedDataFromLoadPresentation),
            CreateEmptyLoadedData());
        var analysisRange = CreateAnalysisRangeState(inputs.Intents, loadedDataState);
        var dampingSpeedCutoffs = CreateDampingSpeedCutoffsState(inputs.Intents);
        var preferenceIntent = CreatePreferenceIntentState(inputs.Intents, inputs.PreferenceReplays);
        var screenState = CreateInputState(
            loadPresentationState.Select(CreateScreenStateFromLoadPresentation),
            SessionScreenPresentationState.Ready);
        var operationState = CreateInputState(inputs.OperationStates, SessionOperationPresentationState.Hidden);
        var mapState = CreateInputState(
            loadPresentationState.Select(CreateMapStateFromLoadPresentation),
            SurfacePresentationState.Hidden);
        var mediaPaneState = CreateInputState(inputs.MediaPaneStates, SurfacePresentationState.Hidden);
        var mediaColumnWidth = CreateInputState(
            loadPresentationState.Select(CreateMediaColumnWidthFromLoadPresentation),
            (double?)null);
        var mediaUrl = CreateInputState(inputs.MediaUrls, (string?)null);
        var analysisPresentation = CreateInputState(
            loadPresentationState.CombineLatest(
                analysisRange,
                static (load, analysis) => CreateAnalysisPresentationFromLoadPresentation(load, analysis.AnalysisRange)),
            CreateHiddenAnalysisPresentationState());
        var dampingPercentageState = CreateInputState(
            inputs.DampingPercentages,
            SessionDampingPercentages.Empty);
        var plotDampingSpeedCutoffState = CreateInputState(
            inputs.PlotDampingSpeedCutoffs,
            DampingSpeedCutoffs.Default);
        var canEditDampingSpeedCutoffState = CreateInputState(
            inputs.CanEditDampingSpeedCutoffs,
            false);
        var sessionInsightsState = CreateInputState(
            inputs.SessionInsights,
            SessionInsightsResult.Hidden);
        var signalAvailabilityState = CreateInputState(
            loadPresentationState.Select(CreateSignalAvailabilityFromLoadPresentation),
            CreateHiddenSignalAvailabilityState());
        var explicitSignalPresentationState = CreateInputState(
            inputs.Intents
                .OfType<RecordedSessionEditorIntent.SetSignalPresentation>()
                .Select(static intent => intent.State),
            CreateHiddenSignalPresentationState());
        var signalPresentationState = loadPresentationState
            .CombineLatest(
                explicitSignalPresentationState,
                CreateSignalPresentationFromLoadPresentation)
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount();
        var analysisSelectionState = CreateAnalysisSelectionState(
            inputs.Intents,
            inputs.AnalysisSelections,
            loadedDataState,
            analysisRange);
        var signalPlotContextMenuActionState = CreateInputState(
            inputs.SignalPlotContextMenuActions,
            CreateEmptySignalPlotContextMenuActions());
        var domainState = CreateOptionalInputState(inputs.DomainStates);
        var derivedIntentState = selectedPageIndex
            .CombineLatest(
                analysisRange,
                static (pageIndex, analysis) => new { pageIndex, analysis })
            .CombineLatest(
                dampingSpeedCutoffs,
                static (current, cutoffs) => new { current.pageIndex, current.analysis, cutoffs })
            .CombineLatest(
                preferenceIntent,
                static (current, preferences) => new DerivedIntentState(
                    current.pageIndex,
                    current.analysis,
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
        var replayingState = Observable.Return(RecordedSessionEditorState.CreateInitial())
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
                loadPresentationState,
                static (current, load) => new { current.state, current.derived, current.presentation, current.media, load })
            .CombineLatest(
                analysisPresentation,
                static (current, analysis) => new { current.state, current.derived, current.presentation, current.media, current.load, analysis })
            .CombineLatest(
                dampingPercentageState,
                static (current, percentages) => new { current.state, current.derived, current.presentation, current.media, current.load, current.analysis, percentages })
            .CombineLatest(
                plotDampingSpeedCutoffState,
                static (current, plotCutoffs) => new { current.state, current.derived, current.presentation, current.media, current.load, current.analysis, current.percentages, plotCutoffs })
            .CombineLatest(
                canEditDampingSpeedCutoffState,
                static (current, canEditCutoffs) => new { current.state, current.derived, current.presentation, current.media, current.load, current.analysis, current.percentages, current.plotCutoffs, canEditCutoffs })
            .CombineLatest(
                sessionInsightsState,
                static (current, insights) => new { current.state, current.derived, current.presentation, current.media, current.load, current.analysis, current.percentages, current.plotCutoffs, current.canEditCutoffs, insights })
            .CombineLatest(
                signalAvailabilityState,
                static (current, signalAvailability) => new { current.state, current.derived, current.presentation, current.media, current.load, current.analysis, current.percentages, current.plotCutoffs, current.canEditCutoffs, current.insights, signalAvailability })
            .CombineLatest(
                signalPresentationState,
                static (current, signals) => new { current.state, current.derived, current.presentation, current.media, current.load, current.analysis, current.percentages, current.plotCutoffs, current.canEditCutoffs, current.insights, current.signalAvailability, signals })
            .CombineLatest(
                analysisSelectionState,
                static (current, analysisSelection) => new { current.state, current.derived, current.presentation, current.media, current.load, current.analysis, current.percentages, current.plotCutoffs, current.canEditCutoffs, current.insights, current.signalAvailability, current.signals, analysisSelection })
            .CombineLatest(
                loadedDataState,
                static (current, loadedData) => new { current.state, current.derived, current.presentation, current.media, current.load, current.analysis, current.percentages, current.plotCutoffs, current.canEditCutoffs, current.insights, current.signalAvailability, current.signals, current.analysisSelection, loadedData })
            .CombineLatest(
                signalPlotContextMenuActionState,
                static (current, signalPlotContextMenuActions) => new
                {
                    current.state,
                    current.derived,
                    current.presentation,
                    current.media,
                    current.load,
                    current.analysis,
                    current.percentages,
                    current.plotCutoffs,
                    current.canEditCutoffs,
                    current.insights,
                    current.signalAvailability,
                    current.signals,
                    current.analysisSelection,
                    current.loadedData,
                    signalPlotContextMenuActions,
                })
            .CombineLatest(
                domainState,
                static (current, domain) =>
                {
                    var loaded = current.loadedData;
                    var session = domain?.Session ?? loaded.Session ?? current.state.Session;

                    return current.state with
                    {
                        Domain = domain ?? current.state.Domain,
                        Load = current.load,
                        Session = session,
                        TelemetryData = loaded.TelemetryData,
                        FullTrackPoints = loaded.FullTrackPoints,
                        TrackPoints = loaded.TrackPoints,
                        TrackTimelineContext = loaded.TrackTimelineContext,
                        Preferences = current.derived.Preferences,
                        Intent = current.state.Intent with
                        {
                            SelectedPageIndex = current.derived.SelectedPageIndex,
                            AnalysisRange = ClampAnalysisRange(current.derived.Analysis.AnalysisRange, loaded.TelemetryData),
                            PendingAnalysisRangeBoundary = current.derived.Analysis.PendingAnalysisRangeBoundary,
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
                            SignalAvailability = current.signalAvailability,
                            Signals = current.signals,
                            Analysis = current.analysis,
                            DampingPercentages = current.percentages,
                            PlotDampingSpeedCutoffs = current.plotCutoffs,
                            CanEditDampingSpeedCutoffs = current.canEditCutoffs,
                            SessionInsights = current.insights,
                            SignalPlotContextMenuActionsBySignalRowId = current.signalPlotContextMenuActions,
                            ScreenState = current.presentation.ScreenState,
                            OperationState = current.presentation.OperationState,
                        },
                        AnalysisSelection = current.analysisSelection,
                    };
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

    private static IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> CreateEmptySignalPlotContextMenuActions()
    {
        return new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>();
    }

    private static RecordedSessionLoadedData CreateLoadedDataFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        if (load is not RecordedSessionLoadPresentation.Loaded loaded)
        {
            return new RecordedSessionLoadedData(
                GetSessionSnapshot(load),
                TelemetryData: null,
                FullTrackPoints: null,
                TrackPoints: null,
                TrackTimelineContext: null);
        }

        var telemetry = loaded.Data.TelemetryPresentation;
        var session = loaded.Session;
        return new RecordedSessionLoadedData(
            Session: session,
            TelemetryData: telemetry.TelemetryData,
            FullTrackPoints: telemetry.FullTrackPoints,
            TrackPoints: telemetry.TrackPoints,
            TrackTimelineContext: CreateTrackTimelineContext(
                telemetry.TelemetryData,
                telemetry.TrackPoints,
                session));
    }

    private static RecordedSessionLoadedData CreateEmptyLoadedData()
    {
        return new RecordedSessionLoadedData(
            Session: null,
            TelemetryData: null,
            FullTrackPoints: null,
            TrackPoints: null,
            TrackTimelineContext: null);
    }

    private static SessionSnapshot? GetSessionSnapshot(RecordedSessionLoadPresentation load)
    {
        return load switch
        {
            RecordedSessionLoadPresentation.Empty empty => empty.Session,
            RecordedSessionLoadPresentation.Loading loading => loading.Session,
            RecordedSessionLoadPresentation.Loaded loaded => loaded.Session,
            RecordedSessionLoadPresentation.IncompleteLocalData incomplete => incomplete.Session,
            RecordedSessionLoadPresentation.Failed failed => failed.Session,
            _ => null,
        };
    }

    private static TrackTimeRange? CreateTrackTimelineContext(
        TelemetryData? telemetry,
        IReadOnlyList<TrackPoint>? trackPoints,
        SessionSnapshot? session)
    {
        return telemetry is null
            ? null
            : TrackPointSeries.BuildTimelineContext(
                trackPoints,
                telemetry.Metadata.Timestamp + SessionTrackProjection.NormalizeGpsOffsetSeconds(session?.GpsOffsetSeconds ?? 0),
                telemetry.Metadata.Duration);
    }

    private static SessionScreenPresentationState CreateScreenStateFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        return load switch
        {
            RecordedSessionLoadPresentation.IncompleteLocalData incomplete =>
                SessionScreenPresentationState.IncompleteLocalData(
                    FormatIncompleteLocalDataMessage(incomplete.Missing)),
            RecordedSessionLoadPresentation.Failed failed =>
                SessionScreenPresentationState.Error($"Could not load session data: {failed.ErrorMessage}"),
            _ => SessionScreenPresentationState.Ready,
        };
    }

    private static SurfacePresentationState CreateMapStateFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        return load switch
        {
            RecordedSessionLoadPresentation.Loading loading => loading.MapExpected
                ? SurfacePresentationState.Loading("Loading map data.")
                : SurfacePresentationState.Hidden,
            RecordedSessionLoadPresentation.Loaded loaded =>
                RecordedSessionPresentationDeriver.CreateMapState(
                    loaded.Data.TelemetryPresentation.TrackPoints,
                    loaded.Data.TelemetryPresentation.FullTrackId is not null),
            _ => SurfacePresentationState.Hidden,
        };
    }

    private static double? CreateMediaColumnWidthFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        return load is RecordedSessionLoadPresentation.Loaded loaded
            ? loaded.Data.TelemetryPresentation.MediaColumnWidth
            : null;
    }

    private static RecordedSignalAvailabilityState CreateSignalAvailabilityFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        return load is RecordedSessionLoadPresentation.Loaded loaded
            ? RecordedSessionPresentationDeriver.CreateSignalAvailability(
                loaded.Data.TelemetryPresentation.TelemetryData,
                loaded.Data.TelemetryPresentation.TrackPoints)
            : CreateHiddenSignalAvailabilityState();
    }

    private static RecordedSignalPresentationState CreateSignalPresentationFromLoadPresentation(
        RecordedSessionLoadPresentation load,
        RecordedSignalPresentationState explicitState)
    {
        if (load is RecordedSessionLoadPresentation.Empty)
        {
            return explicitState;
        }

        return PreserveSignalPresentationOverlays(
            CreateSignalPresentationFromLoadPresentation(load),
            explicitState);
    }

    private static RecordedSignalPresentationState CreateSignalPresentationFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        return load switch
        {
            RecordedSessionLoadPresentation.Loading loading =>
                RecordedSessionPresentationDeriver.CreateLoadingSignalPresentation(loading.MapExpected),
            RecordedSessionLoadPresentation.Loaded loaded =>
                RecordedSessionPresentationDeriver.CreateSignalPresentation(
                    loaded.Data.TelemetryPresentation.TelemetryData,
                    loaded.Data.TelemetryPresentation.TrackPoints),
            _ => CreateHiddenSignalPresentationState(),
        };
    }

    private static RecordedSignalPresentationState PreserveSignalPresentationOverlays(
        RecordedSignalPresentationState derived,
        RecordedSignalPresentationState explicitState)
    {
        return derived with
        {
            ShowAirtime = explicitState.ShowAirtime,
            ShowVelocityAirtime = explicitState.ShowVelocityAirtime,
            ShowImuAirtime = explicitState.ShowImuAirtime,
            ShowPitchRollAirtime = explicitState.ShowPitchRollAirtime,
            ShowSpeedAirtime = explicitState.ShowSpeedAirtime,
            ShowElevationAirtime = explicitState.ShowElevationAirtime,
            ShowAnalysisSelection = explicitState.ShowAnalysisSelection,
            ShowVelocityAnalysisSelection = explicitState.ShowVelocityAnalysisSelection,
            ShowImuAnalysisSelection = explicitState.ShowImuAnalysisSelection,
            ShowPitchRollAnalysisSelection = explicitState.ShowPitchRollAnalysisSelection,
            ShowSpeedAnalysisSelection = explicitState.ShowSpeedAnalysisSelection,
            ShowElevationAnalysisSelection = explicitState.ShowElevationAnalysisSelection,
            TravelHeaderActions = explicitState.TravelHeaderActions,
            VelocityHeaderActions = explicitState.VelocityHeaderActions,
            ImuHeaderActions = explicitState.ImuHeaderActions,
            PitchRollHeaderActions = explicitState.PitchRollHeaderActions,
            SpeedHeaderActions = explicitState.SpeedHeaderActions,
            ElevationHeaderActions = explicitState.ElevationHeaderActions,
        };
    }

    private static RecordedAnalysisPresentationState CreateAnalysisPresentationFromLoadPresentation(
        RecordedSessionLoadPresentation load,
        TelemetryTimeRange? analysisRange)
    {
        if (load is RecordedSessionLoadPresentation.Loading)
        {
            return RecordedSessionPresentationDeriver.CreateLoadingAnalysisPresentationState();
        }

        if (load is not RecordedSessionLoadPresentation.Loaded loaded)
        {
            return CreateHiddenAnalysisPresentationState();
        }

        return RecordedSessionPresentationDeriver.CreateAnalysisPresentation(
            loaded.Data.TelemetryPresentation.TelemetryData,
            analysisRange,
            HasFrontCacheAnalysis(loaded.Data.CachePresentation),
            HasRearCacheAnalysis(loaded.Data.CachePresentation),
            loaded.Data.CachePresentation.BalanceAvailable);
    }

    private static bool HasFrontCacheAnalysis(SessionCachePresentationData data)
    {
        return !string.IsNullOrWhiteSpace(data.FrontTravelDistribution)
               || !string.IsNullOrWhiteSpace(data.FrontVelocityDistribution);
    }

    private static bool HasRearCacheAnalysis(SessionCachePresentationData data)
    {
        return !string.IsNullOrWhiteSpace(data.RearTravelDistribution)
               || !string.IsNullOrWhiteSpace(data.RearVelocityDistribution);
    }

    private static string FormatIncompleteLocalDataMessage(MissingSessionData missing)
    {
        var missingParts = new List<string>();
        if (missing.ProcessedTelemetryBlob)
        {
            missingParts.Add("processed telemetry");
        }

        if (missing.RecordedSourceMissingOrHashMismatch)
        {
            missingParts.Add("recorded source");
        }

        return missingParts.Count == 0
            ? "Local session data is incomplete. Run sync and try again."
            : $"Local session data is incomplete: {string.Join(", ", missingParts)}. Run sync and try again.";
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

    private static RecordedSignalAvailabilityState CreateHiddenSignalAvailabilityState()
    {
        return new RecordedSignalAvailabilityState(
            Travel: false,
            Velocity: false,
            Imu: false,
            PitchRoll: false,
            Speed: false,
            Elevation: false);
    }

    private static IObservable<T> CreateInputState<T>(IObservable<T> updates, T initialValue)
    {
        return updates
            .StartWith(initialValue)
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount();
    }

    private static IObservable<T?> CreateOptionalInputState<T>(IObservable<T> updates)
        where T : class
    {
        return updates
            .Select(static value => (T?)value)
            .StartWith((T?)null)
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount();
    }

    private static IObservable<AnalysisRangeIntentState> CreateAnalysisRangeState(
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<RecordedSessionLoadedData?> loadedDataStates)
    {
        var updates = intents
            .WithLatestFrom(
                loadedDataStates,
                static (intent, loadedData) => CreateAnalysisRangeUpdate(intent, loadedData?.TelemetryData))
            .Where(static update => update is not null)
            .Select(static update => update!);

        return updates
            .StartWith(new Func<AnalysisRangeIntentState, AnalysisRangeIntentState>(static current => current))
            .Scan(AnalysisRangeIntentState.Empty, static (current, update) => update(current))
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount();
    }

    private static Func<AnalysisRangeIntentState, AnalysisRangeIntentState>? CreateAnalysisRangeUpdate(
        RecordedSessionEditorIntent intent,
        TelemetryData? telemetryData)
    {
        return intent switch
        {
            RecordedSessionEditorIntent.SetAnalysisRange set =>
                _ => new AnalysisRangeIntentState(set.Range, PendingAnalysisRangeBoundary: null),
            RecordedSessionEditorIntent.ClearAnalysisRange =>
                _ => AnalysisRangeIntentState.Empty,
            RecordedSessionEditorIntent.SetAnalysisRangeBoundary set =>
                current => ApplyAnalysisRangeBoundary(current, set.Seconds, telemetryData, AnalysisRangeBoundaryMode.Nearest),
            RecordedSessionEditorIntent.SetAnalysisRangeStartBoundary set =>
                current => ApplyAnalysisRangeBoundary(current, set.Seconds, telemetryData, AnalysisRangeBoundaryMode.Start),
            RecordedSessionEditorIntent.SetAnalysisRangeEndBoundary set =>
                current => ApplyAnalysisRangeBoundary(current, set.Seconds, telemetryData, AnalysisRangeBoundaryMode.End),
            _ => null,
        };
    }

    private static AnalysisRangeIntentState ApplyAnalysisRangeBoundary(
        AnalysisRangeIntentState current,
        double boundarySeconds,
        TelemetryData? telemetryData,
        AnalysisRangeBoundaryMode mode)
    {
        if (telemetryData is null ||
            !TelemetryTimeRange.TryClampBoundary(
                boundarySeconds,
                telemetryData.Metadata.Duration,
                out var clampedBoundarySeconds))
        {
            return current with { PendingAnalysisRangeBoundary = null };
        }

        var range = ClampAnalysisRange(current.AnalysisRange, telemetryData);
        if (range is { } currentRange)
        {
            return mode switch
            {
                AnalysisRangeBoundaryMode.Start => CreateAnalysisRangeState(current, clampedBoundarySeconds, currentRange.EndSeconds, telemetryData),
                AnalysisRangeBoundaryMode.End => CreateAnalysisRangeState(current, currentRange.StartSeconds, clampedBoundarySeconds, telemetryData),
                _ when Math.Abs(clampedBoundarySeconds - currentRange.StartSeconds) <=
                       Math.Abs(clampedBoundarySeconds - currentRange.EndSeconds) =>
                    CreateAnalysisRangeState(current, clampedBoundarySeconds, currentRange.EndSeconds, telemetryData),
                _ => CreateAnalysisRangeState(current, currentRange.StartSeconds, clampedBoundarySeconds, telemetryData),
            };
        }

        if (current.PendingAnalysisRangeBoundary is not { } pendingBoundary)
        {
            return new AnalysisRangeIntentState(AnalysisRange: null, PendingAnalysisRangeBoundary: clampedBoundarySeconds);
        }

        return mode switch
        {
            AnalysisRangeBoundaryMode.Start => CreateAnalysisRangeState(current, clampedBoundarySeconds, pendingBoundary, telemetryData),
            AnalysisRangeBoundaryMode.End => CreateAnalysisRangeState(current, pendingBoundary, clampedBoundarySeconds, telemetryData),
            _ => CreateAnalysisRangeState(current, pendingBoundary, clampedBoundarySeconds, telemetryData),
        };
    }

    private static AnalysisRangeIntentState CreateAnalysisRangeState(
        AnalysisRangeIntentState current,
        double startSeconds,
        double endSeconds,
        TelemetryData telemetryData)
    {
        return TelemetryTimeRange.TryCreateClamped(
                startSeconds,
                endSeconds,
                telemetryData.Metadata.Duration,
                out var range)
            ? new AnalysisRangeIntentState(range, PendingAnalysisRangeBoundary: null)
            : current with
            {
                AnalysisRange = ClampAnalysisRange(current.AnalysisRange, telemetryData),
                PendingAnalysisRangeBoundary = null,
            };
    }

    private static IObservable<AnalysisSelectionState> CreateAnalysisSelectionState(
        IObservable<RecordedSessionEditorIntent> intents,
        IObservable<AnalysisSelectionState> analysisSelectionStates,
        IObservable<RecordedSessionLoadedData?> loadedDataStates,
        IObservable<AnalysisRangeIntentState> analysisRangeStates)
    {
        var context = loadedDataStates
            .CombineLatest(
                analysisRangeStates,
                static (loadedData, analysisRange) => new AnalysisSelectionContext(
                    loadedData?.TelemetryData,
                    ClampAnalysisRange(analysisRange.AnalysisRange, loadedData?.TelemetryData)));

        var intentUpdates = intents
            .WithLatestFrom(
                context,
                static (intent, selectionContext) => CreateAnalysisSelectionUpdate(
                    intent,
                    selectionContext.TelemetryData,
                    selectionContext.AnalysisRange))
            .Where(static update => update is not null)
            .Select(static update => update!);

        var inputUpdates = analysisSelectionStates
            .Select(static state => new Func<AnalysisSelectionState, AnalysisSelectionState>(_ => state));

        return inputUpdates
            .Merge(intentUpdates)
            .StartWith(new Func<AnalysisSelectionState, AnalysisSelectionState>(static current => current))
            .Scan(CreateEmptyAnalysisSelectionState(), static (current, update) => update(current))
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount();
    }

    private static Func<AnalysisSelectionState, AnalysisSelectionState>? CreateAnalysisSelectionUpdate(
        RecordedSessionEditorIntent intent,
        TelemetryData? telemetryData,
        TelemetryTimeRange? analysisRange)
    {
        return intent switch
        {
            RecordedSessionEditorIntent.SelectAnalysisRange select =>
                current => SelectAnalysisRange(current, select.Selection, telemetryData, analysisRange),
            RecordedSessionEditorIntent.ClearAnalysisSelection =>
                _ => CreateEmptyAnalysisSelectionState(),
            _ => null,
        };
    }

    private static AnalysisSelectionState SelectAnalysisRange(
        AnalysisSelectionState current,
        TelemetryRangeSelection selection,
        TelemetryData? telemetryData,
        TelemetryTimeRange? analysisRange)
    {
        var controller = new AnalysisSelectionController(
            current.ActiveFront,
            current.ActiveRear,
            current.HighlightRanges);

        return controller.Select(selection, telemetryData, analysisRange)
            ? CreateAnalysisSelectionState(controller)
            : current;
    }

    private static AnalysisSelectionState CreateAnalysisSelectionState(AnalysisSelectionController controller)
    {
        return new AnalysisSelectionState(
            controller.ActiveFrontAnalysisSelection,
            controller.ActiveRearAnalysisSelection,
            controller.HighlightRanges);
    }

    private static AnalysisSelectionState CreateEmptyAnalysisSelectionState()
    {
        return new AnalysisSelectionState(
            ActiveFront: null,
            ActiveRear: null,
            HighlightRanges: []);
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
            .Select(RecordedSessionEditorIntentReducers.CreatePreferenceUpdate)
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

    private sealed record AnalysisRangeIntentState(
        TelemetryTimeRange? AnalysisRange,
        double? PendingAnalysisRangeBoundary)
    {
        public static AnalysisRangeIntentState Empty { get; } = new(
            AnalysisRange: null,
            PendingAnalysisRangeBoundary: null);
    }

    private enum AnalysisRangeBoundaryMode
    {
        Nearest,
        Start,
        End,
    }

    private sealed record AnalysisSelectionContext(
        TelemetryData? TelemetryData,
        TelemetryTimeRange? AnalysisRange);

    private sealed record DerivedIntentState(
        int SelectedPageIndex,
        AnalysisRangeIntentState Analysis,
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
