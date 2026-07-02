using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Shared.Base;
namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

public sealed record TravelDistributionModeOption(TravelDistributionMode Value, string DisplayName, string Description);
public sealed record BalanceDisplacementModeOption(BalanceDisplacementMode Value, string DisplayName, string Description);
public sealed record BalanceSpeedModeOption(BalanceSpeedMode Value, string DisplayName, string Description);
public sealed record VelocityAverageModeOption(VelocityAverageMode Value, string DisplayName, string Description);

public interface ISessionShellMobileWorkspace
{
    /// <summary>
    /// The owning editor. The mobile shell's chrome (title, error bar,
    /// back/save/reset/delete line) binds editor-level members through this,
    /// while the rest of the shell binds the workspace surface.
    /// </summary>
    TabPageViewModelBase Editor { get; }

    ObservableCollection<PageViewModelBase> Pages { get; }
    int SelectedPageIndex { get; set; }
    PageViewModelBase? SelectedPage { get; }
    int PageCount { get; }
    string SelectedPageDisplayName { get; }
    SessionScreenPresentationState ScreenState { get; }
    SessionOperationPresentationState SessionOperationState { get; }
}

public interface IRecordedSessionSignalsWorkspace
{
    TelemetryData? TelemetryData { get; }
    TelemetryTimeRange? AnalysisRange { get; }
    IReadOnlyList<TrackPoint>? TrackPoints { get; }
    TrackTimeRange? TrackTimelineContext { get; }
    SurfacePresentationState TravelSignalState { get; }
    SurfacePresentationState VelocitySignalState { get; }
    SurfacePresentationState ImuSignalState { get; }
    SurfacePresentationState PitchRollSignalState { get; }
    SurfacePresentationState SpeedSignalState { get; }
    SurfacePresentationState ElevationSignalState { get; }
    SignalDisplayPreferences SignalDisplayPreferences { get; }
    SignalLayoutPreferences SignalLayoutPreferences { get; set; }
    TelemetrySourceVisibilityStore SourceVisibility { get; }
    SessionTimelineLinkViewModel Timeline { get; }
    RecordedSessionExtensionSlots ExtensionSlots { get; }
    IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId { get; }
    bool ShowAirtime { get; }
    bool ShowVelocityAirtime { get; }
    bool ShowImuAirtime { get; }
    bool ShowPitchRollAirtime { get; }
    bool ShowSpeedAirtime { get; }
    bool ShowElevationAirtime { get; }
    IReadOnlyList<TelemetryHighlightRange> AnalysisSelectionHighlightRanges { get; }
    bool HasAnalysisSelection { get; }
    bool ShowAnalysisSelection { get; }
    bool ShowVelocityAnalysisSelection { get; }
    bool ShowImuAnalysisSelection { get; }
    bool ShowPitchRollAnalysisSelection { get; }
    bool ShowSpeedAnalysisSelection { get; }
    bool ShowElevationAnalysisSelection { get; }
    IReadOnlyList<SignalRowAction> TravelHeaderActions { get; }
    IReadOnlyList<SignalRowAction> VelocityHeaderActions { get; }
    IReadOnlyList<SignalRowAction> ImuHeaderActions { get; }
    IReadOnlyList<SignalRowAction> PitchRollHeaderActions { get; }
    IReadOnlyList<SignalRowAction> SpeedHeaderActions { get; }
    IReadOnlyList<SignalRowAction> ElevationHeaderActions { get; }
    void SetAnalysisRange(double startSeconds, double endSeconds);
    void ClearAnalysisRange();
    void SetAnalysisRangeBoundary(double boundarySeconds);
}

public interface ISessionMediaWorkspace
{
    bool HasMediaContent { get; }
    MapViewModel? MapViewModel { get; }
    SurfacePresentationState MapState { get; }
    SurfacePresentationState MediaPaneState { get; }
    SessionTimelineLinkViewModel Timeline { get; }
    RecordedSessionExtensionSlots ExtensionSlots { get; }
    double? MediaColumnWidth { get; }
    string? MediaUrl { get; }
}

public interface ISessionAnalysisWorkspace
{
    TelemetryData? TelemetryData { get; }
    TelemetryTimeRange? AnalysisRange { get; }
    TravelDistributionMode SelectedTravelDistributionMode { get; set; }
    BalanceDisplacementMode SelectedBalanceDisplacementMode { get; set; }
    BalanceSpeedMode SelectedBalanceSpeedMode { get; set; }
    VelocityAverageMode SelectedVelocityAverageMode { get; set; }
    SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile { get; set; }
    RecordedSessionExtensionSlots ExtensionSlots { get; }
    IReadOnlyList<TravelDistributionModeOption> TravelDistributionModeOptions { get; }
    IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; }
    IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; }
    IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; }
    IReadOnlyList<SessionInsightsTargetProfileOption> SessionInsightsTargetProfileOptions { get; }
    string SessionAnalysisRangeText { get; }
    string SessionAnalysisModesText { get; }
    SurfacePresentationState FrontAnalysisState { get; }
    SurfacePresentationState RearAnalysisState { get; }
    SurfacePresentationState CompressionBalanceState { get; }
    SurfacePresentationState ReboundBalanceState { get; }
    SurfacePresentationState FrontForkVibrationState { get; }
    SurfacePresentationState FrontFrameVibrationState { get; }
    SurfacePresentationState RearForkVibrationState { get; }
    SurfacePresentationState RearFrameVibrationState { get; }
    SessionDampingPercentages DampingPercentages { get; }
    DampingSpeedCutoffs DampingSpeedCutoffs { get; }
    DampingSpeedCutoffs PlotDampingSpeedCutoffs { get; }
    bool CanEditDampingSpeedCutoffs { get; }
    IRecordedSessionAnalysisResultState? AnalysisResultState => null;
    SessionInsightsResult SessionInsights { get; }
    IRelayCommand<TelemetryRangeSelection?> SelectAnalysisRangeCommand { get; }
    TelemetryRangeSelection? ActiveFrontAnalysisSelection { get; }
    TelemetryRangeSelection? ActiveRearAnalysisSelection { get; }
    void PreviewDampingSpeedCutoff(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond);
    void CancelDampingSpeedCutoffPreview();
    Task CommitDampingSpeedCutoffAsync(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond);
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

public interface ILiveSessionSignalsWorkspace
{
    IObservable<LiveSignalBatch> SignalBatches { get; }
    LiveSessionPlotRanges PlotRanges { get; }
    IReadOnlyList<TrackPoint> TrackPoints { get; }
    TrackTimeRange? TrackTimelineContext { get; }
    SurfacePresentationState TravelSignalState { get; }
    SurfacePresentationState VelocitySignalState { get; }
    SurfacePresentationState ImuSignalState { get; }
    SurfacePresentationState PitchRollSignalState { get; }
    SurfacePresentationState SpeedSignalState { get; }
    SurfacePresentationState ElevationSignalState { get; }
    SignalDisplayPreferences SignalDisplayPreferences { get; }
    SignalLayoutPreferences SignalLayoutPreferences { get; set; }
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
