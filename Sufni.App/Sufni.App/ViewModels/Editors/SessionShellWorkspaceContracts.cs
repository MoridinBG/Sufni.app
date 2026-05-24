using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

public sealed record TravelHistogramModeOption(TravelHistogramMode Value, string DisplayName, string Description);
public sealed record BalanceDisplacementModeOption(BalanceDisplacementMode Value, string DisplayName, string Description);
public sealed record BalanceSpeedModeOption(BalanceSpeedMode Value, string DisplayName, string Description);
public sealed record VelocityAverageModeOption(VelocityAverageMode Value, string DisplayName, string Description);

public interface ISessionShellMobileWorkspace
{
    ObservableCollection<PageViewModelBase> Pages { get; }
    SessionScreenPresentationState ScreenState { get; }
}

public interface IRecordedSessionGraphWorkspace
{
    TelemetryData? TelemetryData { get; }
    TelemetryTimeRange? AnalysisRange { get; }
    IReadOnlyList<TrackPoint>? TrackPoints { get; }
    TrackTimeRange? TrackTimelineContext { get; }
    SurfacePresentationState TravelGraphState { get; }
    SurfacePresentationState VelocityGraphState { get; }
    SurfacePresentationState ImuGraphState { get; }
    SurfacePresentationState PitchRollGraphState { get; }
    SurfacePresentationState SpeedGraphState { get; }
    SurfacePresentationState ElevationGraphState { get; }
    SessionPlotPreferences PlotPreferences { get; }
    SessionGraphPreferences GraphPreferences { get; set; }
    TelemetrySourceVisibilityStore SourceVisibility { get; }
    SessionTimelineLinkViewModel Timeline { get; }
    bool ShowAirtime { get; }
    bool ShowVelocityAirtime { get; }
    bool ShowImuAirtime { get; }
    bool ShowPitchRollAirtime { get; }
    bool ShowSpeedAirtime { get; }
    bool ShowElevationAirtime { get; }
    IReadOnlyList<TelemetryPlotRowAction> TravelHeaderActions { get; }
    IReadOnlyList<TelemetryPlotRowAction> VelocityHeaderActions { get; }
    IReadOnlyList<TelemetryPlotRowAction> ImuHeaderActions { get; }
    IReadOnlyList<TelemetryPlotRowAction> PitchRollHeaderActions { get; }
    IReadOnlyList<TelemetryPlotRowAction> SpeedHeaderActions { get; }
    IReadOnlyList<TelemetryPlotRowAction> ElevationHeaderActions { get; }
    void SetAnalysisRange(double startSeconds, double endSeconds);
    void ClearAnalysisRange();
    void SetAnalysisRangeBoundary(double boundarySeconds);
}

public interface ISessionMediaWorkspace
{
    bool HasMediaContent { get; }
    MapViewModel? MapViewModel { get; }
    SurfacePresentationState MapState { get; }
    SurfacePresentationState VideoState { get; }
    SessionTimelineLinkViewModel Timeline { get; }
    double? MapVideoWidth { get; }
    string? VideoUrl { get; }
}

public interface ISessionStatisticsWorkspace
{
    TelemetryData? TelemetryData { get; }
    TelemetryTimeRange? AnalysisRange { get; }
    TravelHistogramMode SelectedTravelHistogramMode { get; set; }
    BalanceDisplacementMode SelectedBalanceDisplacementMode { get; set; }
    BalanceSpeedMode SelectedBalanceSpeedMode { get; set; }
    VelocityAverageMode SelectedVelocityAverageMode { get; set; }
    SessionAnalysisTargetProfile SelectedSessionAnalysisTargetProfile { get; set; }
    IReadOnlyList<TravelHistogramModeOption> TravelHistogramModeOptions { get; }
    IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; }
    IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; }
    IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; }
    IReadOnlyList<SessionAnalysisTargetProfileOption> SessionAnalysisTargetProfileOptions { get; }
    string SessionAnalysisRangeText { get; }
    string SessionAnalysisModesText { get; }
    SurfacePresentationState FrontStatisticsState { get; }
    SurfacePresentationState RearStatisticsState { get; }
    SurfacePresentationState CompressionBalanceState { get; }
    SurfacePresentationState ReboundBalanceState { get; }
    SurfacePresentationState FrontForkVibrationState { get; }
    SurfacePresentationState FrontFrameVibrationState { get; }
    SurfacePresentationState RearForkVibrationState { get; }
    SurfacePresentationState RearFrameVibrationState { get; }
    SessionDamperPercentages DamperPercentages { get; }
    SessionAnalysisResult SessionAnalysis { get; }
}

public interface ISessionSidebarWorkspace
{
    string? Name { get; set; }
    string? DescriptionText { get; set; }
    NotesPageViewModel NotesPage { get; }
    SuspensionSettings ForkSettings { get; }
    SuspensionSettings ShockSettings { get; }
    PreferencesPageViewModel PreferencesPage { get; }
    IAsyncRelayCommand SaveCommand { get; }
    IAsyncRelayCommand ResetCommand { get; }
}

public interface ILiveSessionGraphWorkspace
{
    IObservable<LiveGraphBatch> GraphBatches { get; }
    LiveSessionPlotRanges PlotRanges { get; }
    IReadOnlyList<TrackPoint> TrackPoints { get; }
    TrackTimeRange? TrackTimelineContext { get; }
    SurfacePresentationState TravelGraphState { get; }
    SurfacePresentationState VelocityGraphState { get; }
    SurfacePresentationState ImuGraphState { get; }
    SurfacePresentationState PitchRollGraphState { get; }
    SurfacePresentationState SpeedGraphState { get; }
    SurfacePresentationState ElevationGraphState { get; }
    SessionPlotPreferences PlotPreferences { get; }
    SessionGraphPreferences GraphPreferences { get; set; }
    TelemetrySourceVisibilityStore SourceVisibility { get; }
    SessionTimelineLinkViewModel Timeline { get; }
}

public sealed record LiveSessionPlotRanges(
    double TravelMaximum,
    double VelocityMaximum,
    double ImuMaximum,
    double PitchRollMaximum = 15)
{
    public double VelocityMinimum => -VelocityMaximum;
    public double PitchRollMinimum => -PitchRollMaximum;

    public static readonly LiveSessionPlotRanges Default = new(
        TravelMaximum: 1,
        VelocityMaximum: 5,
        ImuMaximum: 5,
        PitchRollMaximum: 15);
}

public interface ILiveSessionControlsWorkspace
{
    LiveSessionControlState ControlState { get; }
    IAsyncRelayCommand SaveCommand { get; }
    IAsyncRelayCommand ResetCommand { get; }
}
