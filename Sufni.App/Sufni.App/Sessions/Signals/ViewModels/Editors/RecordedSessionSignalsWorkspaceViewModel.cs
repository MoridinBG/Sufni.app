using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.Telemetry;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Signals.ViewModels.Editors;

internal sealed class RecordedSessionSignalsWorkspaceViewModel : ObservableObject, IRecordedSessionSignalsWorkspace, IDisposable
{
    private readonly Func<RecordedSessionExtensionSlots> extensionSlots;
    private readonly RecordedSessionEditorActions actions;
    private readonly IDisposable stateSubscription;
    private TelemetryData? telemetryData;
    private TelemetryTimeRange? analysisRange;
    private IReadOnlyList<TrackPoint>? trackPoints;
    private TrackTimeRange? trackTimelineContext;
    private SurfacePresentationState travelSignalState = SurfacePresentationState.Hidden;
    private SurfacePresentationState velocitySignalState = SurfacePresentationState.Hidden;
    private SurfacePresentationState imuSignalState = SurfacePresentationState.Hidden;
    private SurfacePresentationState pitchRollSignalState = SurfacePresentationState.Hidden;
    private SurfacePresentationState speedSignalState = SurfacePresentationState.Hidden;
    private SurfacePresentationState elevationSignalState = SurfacePresentationState.Hidden;
    private SignalDisplayPreferences signalDisplayPreferences = SessionPreferences.Default.SignalDisplay;
    private SignalLayoutPreferences signalLayoutPreferences = SessionPreferences.Default.SignalLayout;
    private IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> signalPlotContextMenuActionsBySignalRowId =
        new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>();
    private bool showAirtime = true;
    private bool showVelocityAirtime;
    private bool showImuAirtime;
    private bool showPitchRollAirtime;
    private bool showSpeedAirtime;
    private bool showElevationAirtime;
    private IReadOnlyList<TelemetryHighlightRange> analysisSelectionHighlightRanges = [];
    private bool showAnalysisSelection;
    private bool showVelocityAnalysisSelection;
    private bool showImuAnalysisSelection;
    private bool showPitchRollAnalysisSelection;
    private bool showSpeedAnalysisSelection;
    private bool showElevationAnalysisSelection;
    private IReadOnlyList<SignalRowAction> travelHeaderActions = [];
    private IReadOnlyList<SignalRowAction> velocityHeaderActions = [];
    private IReadOnlyList<SignalRowAction> imuHeaderActions = [];
    private IReadOnlyList<SignalRowAction> pitchRollHeaderActions = [];
    private IReadOnlyList<SignalRowAction> speedHeaderActions = [];
    private IReadOnlyList<SignalRowAction> elevationHeaderActions = [];

    public RecordedSessionSignalsWorkspaceViewModel(
        IObservable<RecordedSessionEditorState> state,
        TelemetrySourceVisibilityStore sourceVisibility,
        SessionTimelineLinkViewModel timeline,
        Func<RecordedSessionExtensionSlots> extensionSlots,
        RecordedSessionEditorActions actions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sourceVisibility);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(extensionSlots);
        ArgumentNullException.ThrowIfNull(actions);

        SourceVisibility = sourceVisibility;
        Timeline = timeline;
        this.extensionSlots = extensionSlots;
        this.actions = actions;
        stateSubscription = state.Subscribe(ApplyState);
    }

    public TelemetryData? TelemetryData => telemetryData;

    public TelemetryTimeRange? AnalysisRange => analysisRange;

    public IReadOnlyList<TrackPoint>? TrackPoints => trackPoints;

    public TrackTimeRange? TrackTimelineContext => trackTimelineContext;

    public SurfacePresentationState TravelSignalState => travelSignalState;

    public SurfacePresentationState VelocitySignalState => velocitySignalState;

    public SurfacePresentationState ImuSignalState => imuSignalState;

    public SurfacePresentationState PitchRollSignalState => pitchRollSignalState;

    public SurfacePresentationState SpeedSignalState => speedSignalState;

    public SurfacePresentationState ElevationSignalState => elevationSignalState;

    public SignalDisplayPreferences SignalDisplayPreferences => signalDisplayPreferences;

    public SignalLayoutPreferences SignalLayoutPreferences
    {
        get => signalLayoutPreferences;
        set => actions.SetSignalLayoutPreferences(value);
    }

    public TelemetrySourceVisibilityStore SourceVisibility { get; }

    public SessionTimelineLinkViewModel Timeline { get; }

    public RecordedSessionExtensionSlots ExtensionSlots => extensionSlots();

    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId =>
        signalPlotContextMenuActionsBySignalRowId;

    public bool ShowAirtime => showAirtime;

    public bool ShowVelocityAirtime => showVelocityAirtime;

    public bool ShowImuAirtime => showImuAirtime;

    public bool ShowPitchRollAirtime => showPitchRollAirtime;

    public bool ShowSpeedAirtime => showSpeedAirtime;

    public bool ShowElevationAirtime => showElevationAirtime;

    public IReadOnlyList<TelemetryHighlightRange> AnalysisSelectionHighlightRanges =>
        analysisSelectionHighlightRanges;

    public bool HasAnalysisSelection => AnalysisSelectionHighlightRanges.Count > 0;

    public bool ShowAnalysisSelection => showAnalysisSelection;

    public bool ShowVelocityAnalysisSelection => showVelocityAnalysisSelection;

    public bool ShowImuAnalysisSelection => showImuAnalysisSelection;

    public bool ShowPitchRollAnalysisSelection => showPitchRollAnalysisSelection;

    public bool ShowSpeedAnalysisSelection => showSpeedAnalysisSelection;

    public bool ShowElevationAnalysisSelection => showElevationAnalysisSelection;

    public IReadOnlyList<SignalRowAction> TravelHeaderActions => travelHeaderActions;

    public IReadOnlyList<SignalRowAction> VelocityHeaderActions => velocityHeaderActions;

    public IReadOnlyList<SignalRowAction> ImuHeaderActions => imuHeaderActions;

    public IReadOnlyList<SignalRowAction> PitchRollHeaderActions => pitchRollHeaderActions;

    public IReadOnlyList<SignalRowAction> SpeedHeaderActions => speedHeaderActions;

    public IReadOnlyList<SignalRowAction> ElevationHeaderActions => elevationHeaderActions;

    public void SetAnalysisRange(double startSeconds, double endSeconds)
    {
        if (TelemetryTimeRange.TryCreate(startSeconds, endSeconds, out var range))
        {
            actions.SetAnalysisRange(range);
        }
    }

    public void ClearAnalysisRange()
    {
        actions.ClearAnalysisRange();
    }

    public void SetAnalysisRangeBoundary(double boundarySeconds)
    {
        actions.SetAnalysisRangeBoundary(boundarySeconds);
    }

    public void Dispose()
    {
        stateSubscription.Dispose();
    }

    internal static readonly HashSet<string> ForwardedProperties =
    [
        nameof(TelemetryData),
        nameof(AnalysisRange),
        nameof(TrackPoints),
        nameof(TrackTimelineContext),
        nameof(TravelSignalState),
        nameof(VelocitySignalState),
        nameof(ImuSignalState),
        nameof(PitchRollSignalState),
        nameof(SpeedSignalState),
        nameof(ElevationSignalState),
        nameof(SignalDisplayPreferences),
        nameof(SignalLayoutPreferences),
        nameof(SourceVisibility),
        nameof(Timeline),
        nameof(ExtensionSlots),
        nameof(SignalPlotContextMenuActionsBySignalRowId),
        nameof(ShowAirtime),
        nameof(ShowVelocityAirtime),
        nameof(ShowImuAirtime),
        nameof(ShowPitchRollAirtime),
        nameof(ShowSpeedAirtime),
        nameof(ShowElevationAirtime),
        nameof(AnalysisSelectionHighlightRanges),
        nameof(HasAnalysisSelection),
        nameof(ShowAnalysisSelection),
        nameof(ShowVelocityAnalysisSelection),
        nameof(ShowImuAnalysisSelection),
        nameof(ShowPitchRollAnalysisSelection),
        nameof(ShowSpeedAnalysisSelection),
        nameof(ShowElevationAnalysisSelection),
        nameof(TravelHeaderActions),
        nameof(VelocityHeaderActions),
        nameof(ImuHeaderActions),
        nameof(PitchRollHeaderActions),
        nameof(SpeedHeaderActions),
        nameof(ElevationHeaderActions),
    ];

    private void ApplyState(RecordedSessionEditorState state)
    {
        SetProperty(ref telemetryData, state.TelemetryData, nameof(TelemetryData));
        SetProperty(ref analysisRange, state.Intent.AnalysisRange, nameof(AnalysisRange));
        SetProperty(ref trackPoints, state.TrackPoints, nameof(TrackPoints));
        SetProperty(ref trackTimelineContext, state.TrackTimelineContext, nameof(TrackTimelineContext));
        SetProperty(ref travelSignalState, state.Presentation.Signals.Travel, nameof(TravelSignalState));
        SetProperty(ref velocitySignalState, state.Presentation.Signals.Velocity, nameof(VelocitySignalState));
        SetProperty(ref imuSignalState, state.Presentation.Signals.Imu, nameof(ImuSignalState));
        SetProperty(ref pitchRollSignalState, state.Presentation.Signals.PitchRoll, nameof(PitchRollSignalState));
        SetProperty(ref speedSignalState, state.Presentation.Signals.Speed, nameof(SpeedSignalState));
        SetProperty(ref elevationSignalState, state.Presentation.Signals.Elevation, nameof(ElevationSignalState));
        SetProperty(ref signalDisplayPreferences, state.Intent.SignalDisplayPreferences, nameof(SignalDisplayPreferences));
        SetProperty(ref signalLayoutPreferences, state.Intent.SignalLayoutPreferences, nameof(SignalLayoutPreferences));
        SetProperty(
            ref signalPlotContextMenuActionsBySignalRowId,
            state.Presentation.SignalPlotContextMenuActionsBySignalRowId,
            nameof(SignalPlotContextMenuActionsBySignalRowId));
        SetProperty(ref showAirtime, state.Presentation.Signals.ShowAirtime, nameof(ShowAirtime));
        SetProperty(ref showVelocityAirtime, state.Presentation.Signals.ShowVelocityAirtime, nameof(ShowVelocityAirtime));
        SetProperty(ref showImuAirtime, state.Presentation.Signals.ShowImuAirtime, nameof(ShowImuAirtime));
        SetProperty(ref showPitchRollAirtime, state.Presentation.Signals.ShowPitchRollAirtime, nameof(ShowPitchRollAirtime));
        SetProperty(ref showSpeedAirtime, state.Presentation.Signals.ShowSpeedAirtime, nameof(ShowSpeedAirtime));
        SetProperty(ref showElevationAirtime, state.Presentation.Signals.ShowElevationAirtime, nameof(ShowElevationAirtime));
        if (SetProperty(ref analysisSelectionHighlightRanges, state.AnalysisSelection.HighlightRanges, nameof(AnalysisSelectionHighlightRanges)))
        {
            OnPropertyChanged(nameof(HasAnalysisSelection));
        }

        SetProperty(ref showAnalysisSelection, state.Presentation.Signals.ShowAnalysisSelection, nameof(ShowAnalysisSelection));
        SetProperty(ref showVelocityAnalysisSelection, state.Presentation.Signals.ShowVelocityAnalysisSelection, nameof(ShowVelocityAnalysisSelection));
        SetProperty(ref showImuAnalysisSelection, state.Presentation.Signals.ShowImuAnalysisSelection, nameof(ShowImuAnalysisSelection));
        SetProperty(ref showPitchRollAnalysisSelection, state.Presentation.Signals.ShowPitchRollAnalysisSelection, nameof(ShowPitchRollAnalysisSelection));
        SetProperty(ref showSpeedAnalysisSelection, state.Presentation.Signals.ShowSpeedAnalysisSelection, nameof(ShowSpeedAnalysisSelection));
        SetProperty(ref showElevationAnalysisSelection, state.Presentation.Signals.ShowElevationAnalysisSelection, nameof(ShowElevationAnalysisSelection));
        SetProperty(ref travelHeaderActions, state.Presentation.Signals.TravelHeaderActions, nameof(TravelHeaderActions));
        SetProperty(ref velocityHeaderActions, state.Presentation.Signals.VelocityHeaderActions, nameof(VelocityHeaderActions));
        SetProperty(ref imuHeaderActions, state.Presentation.Signals.ImuHeaderActions, nameof(ImuHeaderActions));
        SetProperty(ref pitchRollHeaderActions, state.Presentation.Signals.PitchRollHeaderActions, nameof(PitchRollHeaderActions));
        SetProperty(ref speedHeaderActions, state.Presentation.Signals.SpeedHeaderActions, nameof(SpeedHeaderActions));
        SetProperty(ref elevationHeaderActions, state.Presentation.Signals.ElevationHeaderActions, nameof(ElevationHeaderActions));
    }
}
