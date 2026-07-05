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
    AnalysisSelectionState AnalysisSelection)
{
    public static RecordedSessionEditorState CreateInitial()
    {
        var preferences = SessionPreferences.Default;
        return new RecordedSessionEditorState(
            Domain: null,
            Session: null,
            TelemetryData: null,
            FullTrackPoints: null,
            TrackPoints: null,
            TrackTimelineContext: null,
            Preferences: preferences,
            Intent: new RecordedSessionEditorIntentState(
                SelectedPageIndex: 0,
                AnalysisRange: null,
                PendingAnalysisRangeBoundary: null,
                SelectedTravelDistributionMode: preferences.Analysis.TravelDistributionMode,
                SelectedBalanceDisplacementMode: preferences.Analysis.BalanceDisplacementMode,
                SelectedBalanceSpeedMode: preferences.Analysis.BalanceSpeedMode,
                SelectedVelocityAverageMode: preferences.Analysis.VelocityAverageMode,
                SelectedSessionInsightsTargetProfile: preferences.Analysis.SessionInsightsTargetProfile,
                DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
                SignalDisplayPreferences: preferences.SignalDisplay,
                SignalLayoutPreferences: preferences.SignalLayout,
                LayoutPreferences: preferences.Layout),
            Presentation: new RecordedSessionEditorPresentationState(
                MapState: SurfacePresentationState.Hidden,
                MediaPaneState: SurfacePresentationState.Hidden,
                MediaColumnWidth: null,
                MediaUrl: null,
                Signals: CreateHiddenSignalPresentationState(),
                Analysis: CreateHiddenAnalysisPresentationState(),
                DampingPercentages: SessionDampingPercentages.Empty,
                PlotDampingSpeedCutoffs: DampingSpeedCutoffs.Default,
                CanEditDampingSpeedCutoffs: false,
                SessionInsights: SessionInsightsResult.Hidden,
                SignalPlotContextMenuActionsBySignalRowId: new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>(),
                ScreenState: SessionScreenPresentationState.Ready,
                OperationState: SessionOperationPresentationState.Hidden),
            AnalysisSelection: new AnalysisSelectionState(
                ActiveFront: null,
                ActiveRear: null,
                HighlightRanges: []));
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
}

internal sealed record RecordedSessionLoadedData(
    SessionSnapshot? Session,
    TelemetryData? TelemetryData,
    IReadOnlyList<TrackPoint>? FullTrackPoints,
    IReadOnlyList<TrackPoint>? TrackPoints,
    TrackTimeRange? TrackTimelineContext);

internal sealed record RecordedSessionEditorIntentState(
    int SelectedPageIndex,
    TelemetryTimeRange? AnalysisRange,
    double? PendingAnalysisRangeBoundary,
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
    double? MediaColumnWidth,
    string? MediaUrl,
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
