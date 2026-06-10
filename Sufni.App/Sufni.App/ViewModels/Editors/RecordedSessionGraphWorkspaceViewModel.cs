using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.ExtensionHost.Presentation;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.ExtensionHost.ViewModels.Editors;
using Sufni.App.ExtensionHost.Views.Controls;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

internal sealed class RecordedSessionGraphWorkspaceViewModel : ObservableObject, IRecordedSessionGraphWorkspace
{
    private readonly RecordedSessionContext context;
    private readonly Action<SessionGraphPreferences> setGraphPreferences;
    private readonly Action<double, double> setAnalysisRange;
    private readonly Action clearAnalysisRange;
    private readonly Action<double> setAnalysisRangeBoundary;

    public RecordedSessionGraphWorkspaceViewModel(
        RecordedSessionContext context,
        Action<SessionGraphPreferences> setGraphPreferences,
        Action<double, double> setAnalysisRange,
        Action clearAnalysisRange,
        Action<double> setAnalysisRangeBoundary)
    {
        this.context = context;
        this.setGraphPreferences = setGraphPreferences;
        this.setAnalysisRange = setAnalysisRange;
        this.clearAnalysisRange = clearAnalysisRange;
        this.setAnalysisRangeBoundary = setAnalysisRangeBoundary;
        context.PropertyChanged += OnContextPropertyChanged;
    }

    public TelemetryData? TelemetryData => context.TelemetryData;

    public TelemetryTimeRange? AnalysisRange => context.AnalysisRange;

    public IReadOnlyList<TrackPoint>? TrackPoints => context.TrackPoints;

    public TrackTimeRange? TrackTimelineContext => context.TrackTimelineContext;

    public SurfacePresentationState TravelGraphState => context.TravelGraphState;

    public SurfacePresentationState VelocityGraphState => context.VelocityGraphState;

    public SurfacePresentationState ImuGraphState => context.ImuGraphState;

    public SurfacePresentationState PitchRollGraphState => context.PitchRollGraphState;

    public SurfacePresentationState SpeedGraphState => context.SpeedGraphState;

    public SurfacePresentationState ElevationGraphState => context.ElevationGraphState;

    public SessionPlotPreferences PlotPreferences => context.PlotPreferences;

    public SessionGraphPreferences GraphPreferences
    {
        get => context.GraphPreferences;
        set => setGraphPreferences(value);
    }

    public TelemetrySourceVisibilityStore SourceVisibility => context.SourceVisibility;

    public SessionTimelineLinkViewModel Timeline => context.Timeline;

    public RecordedSessionExtensionSlots ExtensionSlots => context.ExtensionSlots;

    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> PlotContextMenuActionsByRowId =>
        context.PlotContextMenuActionsByRowId;

    public bool ShowAirtime => context.ShowAirtime;

    public bool ShowVelocityAirtime => context.ShowVelocityAirtime;

    public bool ShowImuAirtime => context.ShowImuAirtime;

    public bool ShowPitchRollAirtime => context.ShowPitchRollAirtime;

    public bool ShowSpeedAirtime => context.ShowSpeedAirtime;

    public bool ShowElevationAirtime => context.ShowElevationAirtime;

    public IReadOnlyList<TelemetryHighlightRange> StatisticsSelectionHighlightRanges =>
        context.StatisticsSelectionHighlightRanges;

    public bool HasStatisticsSelection => context.HasStatisticsSelection;

    public bool ShowStatisticsSelection => context.ShowStatisticsSelection;

    public bool ShowVelocityStatisticsSelection => context.ShowVelocityStatisticsSelection;

    public bool ShowImuStatisticsSelection => context.ShowImuStatisticsSelection;

    public bool ShowPitchRollStatisticsSelection => context.ShowPitchRollStatisticsSelection;

    public bool ShowSpeedStatisticsSelection => context.ShowSpeedStatisticsSelection;

    public bool ShowElevationStatisticsSelection => context.ShowElevationStatisticsSelection;

    public IReadOnlyList<TelemetryPlotRowAction> TravelHeaderActions => context.TravelHeaderActions;

    public IReadOnlyList<TelemetryPlotRowAction> VelocityHeaderActions => context.VelocityHeaderActions;

    public IReadOnlyList<TelemetryPlotRowAction> ImuHeaderActions => context.ImuHeaderActions;

    public IReadOnlyList<TelemetryPlotRowAction> PitchRollHeaderActions => context.PitchRollHeaderActions;

    public IReadOnlyList<TelemetryPlotRowAction> SpeedHeaderActions => context.SpeedHeaderActions;

    public IReadOnlyList<TelemetryPlotRowAction> ElevationHeaderActions => context.ElevationHeaderActions;

    public void SetAnalysisRange(double startSeconds, double endSeconds)
    {
        setAnalysisRange(startSeconds, endSeconds);
    }

    public void ClearAnalysisRange()
    {
        clearAnalysisRange();
    }

    public void SetAnalysisRangeBoundary(double boundarySeconds)
    {
        setAnalysisRangeBoundary(boundarySeconds);
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not null)
        {
            OnPropertyChanged(args.PropertyName);
        }
    }
}
