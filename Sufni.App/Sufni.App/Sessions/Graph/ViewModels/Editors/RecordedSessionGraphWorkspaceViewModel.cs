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
namespace Sufni.App.Sessions.Graph.ViewModels.Editors;

internal sealed class RecordedSessionGraphWorkspaceViewModel : ObservableObject, IRecordedSessionGraphWorkspace
{
    private readonly RecordedSessionContext context;
    private readonly ISessionOperationGateway gateway;

    public RecordedSessionGraphWorkspaceViewModel(
        RecordedSessionContext context,
        ISessionOperationGateway gateway)
    {
        this.context = context;
        this.gateway = gateway;
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
        set => gateway.SetGraphPreferences(value);
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
        gateway.SetAnalysisRange(startSeconds, endSeconds);
    }

    public void ClearAnalysisRange()
    {
        gateway.ClearAnalysisRange();
    }

    public void SetAnalysisRangeBoundary(double boundarySeconds)
    {
        gateway.SetAnalysisRangeBoundary(boundarySeconds);
    }

    internal static readonly HashSet<string> ForwardedProperties =
    [
        nameof(TelemetryData),
        nameof(AnalysisRange),
        nameof(TrackPoints),
        nameof(TrackTimelineContext),
        nameof(TravelGraphState),
        nameof(VelocityGraphState),
        nameof(ImuGraphState),
        nameof(PitchRollGraphState),
        nameof(SpeedGraphState),
        nameof(ElevationGraphState),
        nameof(PlotPreferences),
        nameof(GraphPreferences),
        nameof(SourceVisibility),
        nameof(Timeline),
        nameof(ExtensionSlots),
        nameof(PlotContextMenuActionsByRowId),
        nameof(ShowAirtime),
        nameof(ShowVelocityAirtime),
        nameof(ShowImuAirtime),
        nameof(ShowPitchRollAirtime),
        nameof(ShowSpeedAirtime),
        nameof(ShowElevationAirtime),
        nameof(StatisticsSelectionHighlightRanges),
        nameof(HasStatisticsSelection),
        nameof(ShowStatisticsSelection),
        nameof(ShowVelocityStatisticsSelection),
        nameof(ShowImuStatisticsSelection),
        nameof(ShowPitchRollStatisticsSelection),
        nameof(ShowSpeedStatisticsSelection),
        nameof(ShowElevationStatisticsSelection),
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
