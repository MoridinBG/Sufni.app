using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Store;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal static class RecordedSessionEditorStateSnapshot
{
    public static RecordedSessionEditorState From(
        SessionPreferences preferences,
        IReadOnlyList<SignalRowAction> travelHeaderActions,
        IReadOnlyList<SignalRowAction> velocityHeaderActions,
        IReadOnlyList<SignalRowAction> imuHeaderActions,
        IReadOnlyList<SignalRowAction> pitchRollHeaderActions,
        IReadOnlyList<SignalRowAction> speedHeaderActions,
        IReadOnlyList<SignalRowAction> elevationHeaderActions,
        RecordedSignalToggleState signalToggles,
        RecordedAnalysisModeState analysisModes,
        AnalysisSelectionState analysisSelection,
        SessionScreenPresentationState screenState,
        SessionOperationPresentationState operationState,
        SessionDampingPercentages dampingPercentages,
        IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> signalPlotContextMenuActionsBySignalRowId,
        SessionInsightsResult sessionInsights,
        RecordedSignalSurfaceState signalSurfaces,
        RecordedMediaPresentationState mediaPresentation,
        RecordedAnalysisPresentationState analysisSurfaces,
        DampingSpeedCutoffs dampingSpeedCutoffs,
        DampingSpeedCutoffs plotDampingSpeedCutoffs,
        bool canEditDampingSpeedCutoffs,
        RecordedAnalysisRangeState analysisRangeState,
        RecordedPageSelectionState pageSelectionState,
        RecordedSessionLoadedDataState loadedDataState)
    {
        return new RecordedSessionEditorState(
            Domain: null,
            Session: loadedDataState.Session,
            TelemetryData: loadedDataState.TelemetryData,
            FullTrackPoints: loadedDataState.FullTrackPoints,
            TrackPoints: loadedDataState.TrackPoints,
            TrackTimelineContext: loadedDataState.TrackTimelineContext,
            Preferences: preferences,
            Intent: new RecordedSessionEditorIntentState(
                SelectedPageIndex: pageSelectionState.SelectedPageIndex,
                AnalysisRange: analysisRangeState.AnalysisRange,
                SelectedTravelDistributionMode: analysisModes.SelectedTravelDistributionMode,
                SelectedBalanceDisplacementMode: analysisModes.SelectedBalanceDisplacementMode,
                SelectedBalanceSpeedMode: analysisModes.SelectedBalanceSpeedMode,
                SelectedVelocityAverageMode: analysisModes.SelectedVelocityAverageMode,
                SelectedSessionInsightsTargetProfile: analysisModes.SelectedSessionInsightsTargetProfile,
                DampingSpeedCutoffs: dampingSpeedCutoffs,
                SignalDisplayPreferences: preferences.SignalDisplay,
                SignalLayoutPreferences: preferences.SignalLayout,
                LayoutPreferences: preferences.Layout),
            Presentation: new RecordedSessionEditorPresentationState(
                MapState: mediaPresentation.MapState,
                MediaPaneState: mediaPresentation.MediaPaneState,
                MediaColumnWidth: mediaPresentation.MediaColumnWidth,
                MediaUrl: mediaPresentation.MediaUrl,
                Signals: new RecordedSignalPresentationState(
                    Travel: signalSurfaces.Travel,
                    Velocity: signalSurfaces.Velocity,
                    Imu: signalSurfaces.Imu,
                    PitchRoll: signalSurfaces.PitchRoll,
                    Speed: signalSurfaces.Speed,
                    Elevation: signalSurfaces.Elevation,
                    ShowAirtime: signalToggles.ShowAirtime,
                    ShowVelocityAirtime: signalToggles.ShowVelocityAirtime,
                    ShowImuAirtime: signalToggles.ShowImuAirtime,
                    ShowPitchRollAirtime: signalToggles.ShowPitchRollAirtime,
                    ShowSpeedAirtime: signalToggles.ShowSpeedAirtime,
                    ShowElevationAirtime: signalToggles.ShowElevationAirtime,
                    ShowAnalysisSelection: signalToggles.ShowAnalysisSelection,
                    ShowVelocityAnalysisSelection: signalToggles.ShowVelocityAnalysisSelection,
                    ShowImuAnalysisSelection: signalToggles.ShowImuAnalysisSelection,
                    ShowPitchRollAnalysisSelection: signalToggles.ShowPitchRollAnalysisSelection,
                    ShowSpeedAnalysisSelection: signalToggles.ShowSpeedAnalysisSelection,
                    ShowElevationAnalysisSelection: signalToggles.ShowElevationAnalysisSelection,
                    TravelHeaderActions: travelHeaderActions,
                    VelocityHeaderActions: velocityHeaderActions,
                    ImuHeaderActions: imuHeaderActions,
                    PitchRollHeaderActions: pitchRollHeaderActions,
                    SpeedHeaderActions: speedHeaderActions,
                    ElevationHeaderActions: elevationHeaderActions),
                Analysis: analysisSurfaces,
                DampingPercentages: dampingPercentages,
                PlotDampingSpeedCutoffs: plotDampingSpeedCutoffs,
                CanEditDampingSpeedCutoffs: canEditDampingSpeedCutoffs,
                SessionInsights: sessionInsights,
                SignalPlotContextMenuActionsBySignalRowId: signalPlotContextMenuActionsBySignalRowId,
                ScreenState: screenState,
                OperationState: operationState),
            AnalysisSelection: analysisSelection);
    }
}

internal sealed record RecordedAnalysisRangeState(TelemetryTimeRange? AnalysisRange);

internal sealed record RecordedPageSelectionState(int SelectedPageIndex);

internal sealed record RecordedSessionLoadedDataState(
    SessionSnapshot? Session,
    TelemetryData? TelemetryData,
    IReadOnlyList<TrackPoint>? FullTrackPoints,
    IReadOnlyList<TrackPoint>? TrackPoints,
    TrackTimeRange? TrackTimelineContext);

internal sealed record RecordedAnalysisModeState(
    TravelDistributionMode SelectedTravelDistributionMode,
    BalanceDisplacementMode SelectedBalanceDisplacementMode,
    BalanceSpeedMode SelectedBalanceSpeedMode,
    VelocityAverageMode SelectedVelocityAverageMode,
    SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile);

internal sealed record RecordedSignalSurfaceState(
    SurfacePresentationState Travel,
    SurfacePresentationState Velocity,
    SurfacePresentationState Imu,
    SurfacePresentationState PitchRoll,
    SurfacePresentationState Speed,
    SurfacePresentationState Elevation);

internal sealed record RecordedMediaPresentationState(
    SurfacePresentationState MapState,
    SurfacePresentationState MediaPaneState,
    double? MediaColumnWidth,
    string? MediaUrl);

internal sealed record RecordedSignalToggleState(
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
    bool ShowElevationAnalysisSelection);
