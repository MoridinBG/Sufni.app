using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.Telemetry;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed record RecordedSessionEditorState(
    RecordedSessionDomainSnapshot? Domain,
    SessionSnapshot? Session,
    TelemetryData? TelemetryData,
    IReadOnlyList<TrackPoint>? FullTrackPoints,
    IReadOnlyList<TrackPoint>? TrackPoints,
    TrackTimeRange? TrackTimelineContext,
    SessionPreferences Preferences,
    RecordedSessionEditorIntentState Intent,
    RecordedSessionEditorPresentationState Presentation,
    AnalysisSelectionState AnalysisSelection);

internal sealed record RecordedSessionEditorIntentState(
    int SelectedPageIndex,
    TelemetryTimeRange? AnalysisRange,
    TravelDistributionMode SelectedTravelDistributionMode,
    BalanceDisplacementMode SelectedBalanceDisplacementMode,
    BalanceSpeedMode SelectedBalanceSpeedMode,
    VelocityAverageMode SelectedVelocityAverageMode,
    SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    SignalDisplayPreferences SignalDisplayPreferences,
    SignalLayoutPreferences SignalLayoutPreferences,
    SessionLayoutPreferences LayoutPreferences);

internal sealed record RecordedSessionEditorPresentationState(
    SurfacePresentationState MapState,
    SurfacePresentationState MediaPaneState,
    RecordedSignalPresentationState Signals,
    RecordedAnalysisPresentationState Analysis,
    SessionDampingPercentages DampingPercentages,
    DampingSpeedCutoffs PlotDampingSpeedCutoffs,
    bool CanEditDampingSpeedCutoffs,
    SessionInsightsResult SessionInsights,
    IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId,
    SessionScreenPresentationState ScreenState,
    SessionOperationPresentationState OperationState);

internal sealed record RecordedSignalPresentationState(
    SurfacePresentationState Travel,
    SurfacePresentationState Velocity,
    SurfacePresentationState Imu,
    SurfacePresentationState PitchRoll,
    SurfacePresentationState Speed,
    SurfacePresentationState Elevation,
    bool ShowAirtime,
    bool ShowVelocityAirtime,
    bool ShowImuAirtime,
    bool ShowPitchRollAirtime,
    bool ShowSpeedAirtime,
    bool ShowElevationAirtime,
    bool ShowAnalysisSelection,
    bool ShowVelocityAnalysisSelection,
    bool ShowImuAnalysisSelection,
    bool ShowPitchRollAnalysisSelection,
    bool ShowSpeedAnalysisSelection,
    bool ShowElevationAnalysisSelection,
    IReadOnlyList<SignalRowAction> TravelHeaderActions,
    IReadOnlyList<SignalRowAction> VelocityHeaderActions,
    IReadOnlyList<SignalRowAction> ImuHeaderActions,
    IReadOnlyList<SignalRowAction> PitchRollHeaderActions,
    IReadOnlyList<SignalRowAction> SpeedHeaderActions,
    IReadOnlyList<SignalRowAction> ElevationHeaderActions);

internal sealed record RecordedAnalysisPresentationState(
    SurfacePresentationState FrontAnalysis,
    SurfacePresentationState RearAnalysis,
    SurfacePresentationState CompressionBalance,
    SurfacePresentationState ReboundBalance,
    SurfacePresentationState FrontForkVibration,
    SurfacePresentationState FrontFrameVibration,
    SurfacePresentationState RearForkVibration,
    SurfacePresentationState RearFrameVibration);

internal sealed record AnalysisSelectionState(
    TelemetryRangeSelection? ActiveFront,
    TelemetryRangeSelection? ActiveRear,
    IReadOnlyList<TelemetryHighlightRange> HighlightRanges);
