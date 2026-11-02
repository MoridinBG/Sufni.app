using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Plots;
using Sufni.Telemetry;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Signals.ViewModels.Editors;

internal sealed class RecordedSessionSignalsWorkspaceViewModel : ObservableObject, IRecordedSessionSignalsWorkspace, IDisposable
{
    private readonly RecordedSessionEditorActions actions;
    private readonly IDisposable stateSubscription;
    private RecordedSessionEditorState currentState = RecordedSessionEditorState.CreateInitial();

    public RecordedSessionSignalsWorkspaceViewModel(
        IObservable<RecordedSessionEditorState> state,
        IRecordedSessionAnalysisResultState analysisResultState,
        TelemetrySourceVisibilityStore sourceVisibility,
        SessionTimelineLinkViewModel timeline,
        RecordedSessionEditorActions actions,
        SegmentPickerViewModel segmentPicker,
        SegmentComparisonViewModel segmentComparison)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(analysisResultState);
        ArgumentNullException.ThrowIfNull(sourceVisibility);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(actions);

        AnalysisResultState = analysisResultState;
        SourceVisibility = sourceVisibility;
        Timeline = timeline;
        this.actions = actions;
        SegmentPicker = segmentPicker;
        SegmentComparison = segmentComparison;
        stateSubscription = state.Subscribe(ApplyState);
    }

    public TelemetryData? TelemetryData => currentState.TelemetryData;

    public IRecordedSessionAnalysisResultState AnalysisResultState { get; }

    public TelemetryTimeRange? AnalysisRange => currentState.Intent.AnalysisRange;

    public IReadOnlyList<TrackPoint>? TrackPoints => currentState.TrackPoints;

    public TrackTimeRange? TrackTimelineContext => currentState.TrackTimelineContext;

    public SurfacePresentationState TravelSignalState => currentState.Presentation.Signals.Travel;

    public SurfacePresentationState VelocitySignalState => currentState.Presentation.Signals.Velocity;

    public SurfacePresentationState ImuSignalState => currentState.Presentation.Signals.Imu;

    public SurfacePresentationState PitchRollSignalState => currentState.Presentation.Signals.PitchRoll;

    public SurfacePresentationState SpeedSignalState => currentState.Presentation.Signals.Speed;

    public SurfacePresentationState ElevationSignalState => currentState.Presentation.Signals.Elevation;

    public SignalDisplayPreferences SignalDisplayPreferences => currentState.Preferences.SignalDisplay;

    public SignalLayoutPreferences SignalLayoutPreferences
    {
        get => currentState.Preferences.SignalLayout;
        set => actions.SetSignalLayoutPreferences(value);
    }

    public TelemetrySourceVisibilityStore SourceVisibility { get; }

    public SessionTimelineLinkViewModel Timeline { get; }

    public SegmentPickerViewModel SegmentPicker { get; }
    public SegmentComparisonViewModel SegmentComparison { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId =>
        currentState.Presentation.SignalPlotContextMenuActionsBySignalRowId;

    public bool ShowAirtime => currentState.Presentation.Signals.ShowAirtime;

    public bool ShowVelocityAirtime => currentState.Presentation.Signals.ShowVelocityAirtime;

    public bool ShowImuAirtime => currentState.Presentation.Signals.ShowImuAirtime;

    public bool ShowPitchRollAirtime => currentState.Presentation.Signals.ShowPitchRollAirtime;

    public bool ShowSpeedAirtime => currentState.Presentation.Signals.ShowSpeedAirtime;

    public bool ShowElevationAirtime => currentState.Presentation.Signals.ShowElevationAirtime;

    public IReadOnlyList<TelemetryHighlightRange> AnalysisSelectionHighlightRanges =>
        currentState.AnalysisSelection.HighlightRanges;

    public bool HasAnalysisSelection => AnalysisSelectionHighlightRanges.Count > 0;

    public bool ShowAnalysisSelection => currentState.Presentation.Signals.ShowAnalysisSelection;

    public bool ShowVelocityAnalysisSelection => currentState.Presentation.Signals.ShowVelocityAnalysisSelection;

    public bool ShowImuAnalysisSelection => currentState.Presentation.Signals.ShowImuAnalysisSelection;

    public bool ShowPitchRollAnalysisSelection => currentState.Presentation.Signals.ShowPitchRollAnalysisSelection;

    public bool ShowSpeedAnalysisSelection => currentState.Presentation.Signals.ShowSpeedAnalysisSelection;

    public bool ShowElevationAnalysisSelection => currentState.Presentation.Signals.ShowElevationAnalysisSelection;

    public IReadOnlyList<SignalRowAction> TravelHeaderActions => currentState.Presentation.Signals.TravelHeaderActions;

    public IReadOnlyList<SignalRowAction> VelocityHeaderActions => currentState.Presentation.Signals.VelocityHeaderActions;

    public IReadOnlyList<SignalRowAction> ImuHeaderActions => currentState.Presentation.Signals.ImuHeaderActions;

    public IReadOnlyList<SignalRowAction> PitchRollHeaderActions => currentState.Presentation.Signals.PitchRollHeaderActions;

    public IReadOnlyList<SignalRowAction> SpeedHeaderActions => currentState.Presentation.Signals.SpeedHeaderActions;

    public IReadOnlyList<SignalRowAction> ElevationHeaderActions => currentState.Presentation.Signals.ElevationHeaderActions;

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

    private void ApplyState(RecordedSessionEditorState state)
    {
        var previous = currentState;
        currentState = state;
        NotifyChanged(previous.TelemetryData, state.TelemetryData, nameof(TelemetryData));
        NotifyChanged(previous.Intent.AnalysisRange, state.Intent.AnalysisRange, nameof(AnalysisRange));
        NotifyChanged(previous.TrackPoints, state.TrackPoints, nameof(TrackPoints));
        NotifyChanged(previous.TrackTimelineContext, state.TrackTimelineContext, nameof(TrackTimelineContext));
        NotifyChanged(previous.Presentation.Signals.Travel, state.Presentation.Signals.Travel, nameof(TravelSignalState));
        NotifyChanged(previous.Presentation.Signals.Velocity, state.Presentation.Signals.Velocity, nameof(VelocitySignalState));
        NotifyChanged(previous.Presentation.Signals.Imu, state.Presentation.Signals.Imu, nameof(ImuSignalState));
        NotifyChanged(previous.Presentation.Signals.PitchRoll, state.Presentation.Signals.PitchRoll, nameof(PitchRollSignalState));
        NotifyChanged(previous.Presentation.Signals.Speed, state.Presentation.Signals.Speed, nameof(SpeedSignalState));
        NotifyChanged(previous.Presentation.Signals.Elevation, state.Presentation.Signals.Elevation, nameof(ElevationSignalState));
        NotifyChanged(previous.Preferences.SignalDisplay, state.Preferences.SignalDisplay, nameof(SignalDisplayPreferences));
        NotifyChanged(previous.Preferences.SignalLayout, state.Preferences.SignalLayout, nameof(SignalLayoutPreferences));
        NotifyChanged(previous.Presentation.SignalPlotContextMenuActionsBySignalRowId, state.Presentation.SignalPlotContextMenuActionsBySignalRowId, nameof(SignalPlotContextMenuActionsBySignalRowId));
        NotifyChanged(previous.Presentation.Signals.ShowAirtime, state.Presentation.Signals.ShowAirtime, nameof(ShowAirtime));
        NotifyChanged(previous.Presentation.Signals.ShowVelocityAirtime, state.Presentation.Signals.ShowVelocityAirtime, nameof(ShowVelocityAirtime));
        NotifyChanged(previous.Presentation.Signals.ShowImuAirtime, state.Presentation.Signals.ShowImuAirtime, nameof(ShowImuAirtime));
        NotifyChanged(previous.Presentation.Signals.ShowPitchRollAirtime, state.Presentation.Signals.ShowPitchRollAirtime, nameof(ShowPitchRollAirtime));
        NotifyChanged(previous.Presentation.Signals.ShowSpeedAirtime, state.Presentation.Signals.ShowSpeedAirtime, nameof(ShowSpeedAirtime));
        NotifyChanged(previous.Presentation.Signals.ShowElevationAirtime, state.Presentation.Signals.ShowElevationAirtime, nameof(ShowElevationAirtime));
        if (NotifyChanged(previous.AnalysisSelection.HighlightRanges, state.AnalysisSelection.HighlightRanges, nameof(AnalysisSelectionHighlightRanges)))
        {
            OnPropertyChanged(nameof(HasAnalysisSelection));
        }

        NotifyChanged(previous.Presentation.Signals.ShowAnalysisSelection, state.Presentation.Signals.ShowAnalysisSelection, nameof(ShowAnalysisSelection));
        NotifyChanged(previous.Presentation.Signals.ShowVelocityAnalysisSelection, state.Presentation.Signals.ShowVelocityAnalysisSelection, nameof(ShowVelocityAnalysisSelection));
        NotifyChanged(previous.Presentation.Signals.ShowImuAnalysisSelection, state.Presentation.Signals.ShowImuAnalysisSelection, nameof(ShowImuAnalysisSelection));
        NotifyChanged(previous.Presentation.Signals.ShowPitchRollAnalysisSelection, state.Presentation.Signals.ShowPitchRollAnalysisSelection, nameof(ShowPitchRollAnalysisSelection));
        NotifyChanged(previous.Presentation.Signals.ShowSpeedAnalysisSelection, state.Presentation.Signals.ShowSpeedAnalysisSelection, nameof(ShowSpeedAnalysisSelection));
        NotifyChanged(previous.Presentation.Signals.ShowElevationAnalysisSelection, state.Presentation.Signals.ShowElevationAnalysisSelection, nameof(ShowElevationAnalysisSelection));
        NotifyChanged(previous.Presentation.Signals.TravelHeaderActions, state.Presentation.Signals.TravelHeaderActions, nameof(TravelHeaderActions));
        NotifyChanged(previous.Presentation.Signals.VelocityHeaderActions, state.Presentation.Signals.VelocityHeaderActions, nameof(VelocityHeaderActions));
        NotifyChanged(previous.Presentation.Signals.ImuHeaderActions, state.Presentation.Signals.ImuHeaderActions, nameof(ImuHeaderActions));
        NotifyChanged(previous.Presentation.Signals.PitchRollHeaderActions, state.Presentation.Signals.PitchRollHeaderActions, nameof(PitchRollHeaderActions));
        NotifyChanged(previous.Presentation.Signals.SpeedHeaderActions, state.Presentation.Signals.SpeedHeaderActions, nameof(SpeedHeaderActions));
        NotifyChanged(previous.Presentation.Signals.ElevationHeaderActions, state.Presentation.Signals.ElevationHeaderActions, nameof(ElevationHeaderActions));
    }
    private bool NotifyChanged<T>(T previous, T current, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(previous, current)) return false;
        OnPropertyChanged(propertyName);
        return true;
    }

}
