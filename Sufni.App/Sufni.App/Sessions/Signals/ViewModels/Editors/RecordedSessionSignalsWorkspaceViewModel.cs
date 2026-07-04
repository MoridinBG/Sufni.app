using System;
using System.Collections.Generic;
using System.ComponentModel;
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

internal sealed class RecordedSessionSignalsWorkspaceViewModel : ObservableObject, IRecordedSessionSignalsWorkspace
{
    private readonly RecordedSessionContext context;
    private readonly RecordedSessionEditorActions actions;

    public RecordedSessionSignalsWorkspaceViewModel(
        RecordedSessionContext context,
        RecordedSessionEditorActions actions)
    {
        this.context = context;
        this.actions = actions;
        context.PropertyChanged += OnContextPropertyChanged;
    }

    public TelemetryData? TelemetryData => context.TelemetryData;

    public TelemetryTimeRange? AnalysisRange => context.AnalysisRange;

    public IReadOnlyList<TrackPoint>? TrackPoints => context.TrackPoints;

    public TrackTimeRange? TrackTimelineContext => context.TrackTimelineContext;

    public SurfacePresentationState TravelSignalState => context.TravelSignalState;

    public SurfacePresentationState VelocitySignalState => context.VelocitySignalState;

    public SurfacePresentationState ImuSignalState => context.ImuSignalState;

    public SurfacePresentationState PitchRollSignalState => context.PitchRollSignalState;

    public SurfacePresentationState SpeedSignalState => context.SpeedSignalState;

    public SurfacePresentationState ElevationSignalState => context.ElevationSignalState;

    public SignalDisplayPreferences SignalDisplayPreferences => context.SignalDisplayPreferences;

    public SignalLayoutPreferences SignalLayoutPreferences
    {
        get => context.SignalLayoutPreferences;
        set => actions.SetSignalLayoutPreferences(value);
    }

    public TelemetrySourceVisibilityStore SourceVisibility => context.SourceVisibility;

    public SessionTimelineLinkViewModel Timeline => context.Timeline;

    public RecordedSessionExtensionSlots ExtensionSlots => context.ExtensionSlots;

    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId =>
        context.SignalPlotContextMenuActionsBySignalRowId;

    public bool ShowAirtime => context.ShowAirtime;

    public bool ShowVelocityAirtime => context.ShowVelocityAirtime;

    public bool ShowImuAirtime => context.ShowImuAirtime;

    public bool ShowPitchRollAirtime => context.ShowPitchRollAirtime;

    public bool ShowSpeedAirtime => context.ShowSpeedAirtime;

    public bool ShowElevationAirtime => context.ShowElevationAirtime;

    public IReadOnlyList<TelemetryHighlightRange> AnalysisSelectionHighlightRanges =>
        context.AnalysisSelectionHighlightRanges;

    public bool HasAnalysisSelection => context.HasAnalysisSelection;

    public bool ShowAnalysisSelection => context.ShowAnalysisSelection;

    public bool ShowVelocityAnalysisSelection => context.ShowVelocityAnalysisSelection;

    public bool ShowImuAnalysisSelection => context.ShowImuAnalysisSelection;

    public bool ShowPitchRollAnalysisSelection => context.ShowPitchRollAnalysisSelection;

    public bool ShowSpeedAnalysisSelection => context.ShowSpeedAnalysisSelection;

    public bool ShowElevationAnalysisSelection => context.ShowElevationAnalysisSelection;

    public IReadOnlyList<SignalRowAction> TravelHeaderActions => context.TravelHeaderActions;

    public IReadOnlyList<SignalRowAction> VelocityHeaderActions => context.VelocityHeaderActions;

    public IReadOnlyList<SignalRowAction> ImuHeaderActions => context.ImuHeaderActions;

    public IReadOnlyList<SignalRowAction> PitchRollHeaderActions => context.PitchRollHeaderActions;

    public IReadOnlyList<SignalRowAction> SpeedHeaderActions => context.SpeedHeaderActions;

    public IReadOnlyList<SignalRowAction> ElevationHeaderActions => context.ElevationHeaderActions;

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

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is { } propertyName && ForwardedProperties.Contains(propertyName))
        {
            OnPropertyChanged(propertyName);
        }
    }
}
