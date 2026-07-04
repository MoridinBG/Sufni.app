using System.Collections.Generic;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal static class RecordedSessionEditorStateSnapshot
{
    public static RecordedSessionEditorState From(
        RecordedSessionContext context,
        SessionPreferences preferences,
        IReadOnlyList<SignalRowAction>? travelHeaderActions = null,
        IReadOnlyList<SignalRowAction>? velocityHeaderActions = null,
        IReadOnlyList<SignalRowAction>? imuHeaderActions = null,
        IReadOnlyList<SignalRowAction>? pitchRollHeaderActions = null,
        IReadOnlyList<SignalRowAction>? speedHeaderActions = null,
        IReadOnlyList<SignalRowAction>? elevationHeaderActions = null,
        AnalysisSelectionState? analysisSelection = null)
    {
        return new RecordedSessionEditorState(
            Domain: null,
            Session: context.SessionSnapshot,
            TelemetryData: context.TelemetryData,
            FullTrackPoints: context.FullTrackPoints,
            TrackPoints: context.TrackPoints,
            TrackTimelineContext: context.TrackTimelineContext,
            Preferences: preferences,
            Intent: new RecordedSessionEditorIntentState(
                SelectedPageIndex: context.SelectedPageIndex,
                AnalysisRange: context.AnalysisRange,
                SelectedTravelDistributionMode: context.SelectedTravelDistributionMode,
                SelectedBalanceDisplacementMode: context.SelectedBalanceDisplacementMode,
                SelectedBalanceSpeedMode: context.SelectedBalanceSpeedMode,
                SelectedVelocityAverageMode: context.SelectedVelocityAverageMode,
                SelectedSessionInsightsTargetProfile: context.SelectedSessionInsightsTargetProfile,
                DampingSpeedCutoffs: context.DampingSpeedCutoffs,
                SignalDisplayPreferences: preferences.SignalDisplay,
                SignalLayoutPreferences: preferences.SignalLayout,
                LayoutPreferences: preferences.Layout),
            Presentation: new RecordedSessionEditorPresentationState(
                MapState: context.MapState,
                MediaPaneState: context.MediaPaneState,
                MediaColumnWidth: context.MediaColumnWidth,
                MediaUrl: context.MediaUrl,
                Signals: new RecordedSignalPresentationState(
                    Travel: context.TravelSignalState,
                    Velocity: context.VelocitySignalState,
                    Imu: context.ImuSignalState,
                    PitchRoll: context.PitchRollSignalState,
                    Speed: context.SpeedSignalState,
                    Elevation: context.ElevationSignalState,
                    ShowAirtime: context.ShowAirtime,
                    ShowVelocityAirtime: context.ShowVelocityAirtime,
                    ShowImuAirtime: context.ShowImuAirtime,
                    ShowPitchRollAirtime: context.ShowPitchRollAirtime,
                    ShowSpeedAirtime: context.ShowSpeedAirtime,
                    ShowElevationAirtime: context.ShowElevationAirtime,
                    ShowAnalysisSelection: context.ShowAnalysisSelection,
                    ShowVelocityAnalysisSelection: context.ShowVelocityAnalysisSelection,
                    ShowImuAnalysisSelection: context.ShowImuAnalysisSelection,
                    ShowPitchRollAnalysisSelection: context.ShowPitchRollAnalysisSelection,
                    ShowSpeedAnalysisSelection: context.ShowSpeedAnalysisSelection,
                    ShowElevationAnalysisSelection: context.ShowElevationAnalysisSelection,
                    TravelHeaderActions: travelHeaderActions ?? context.TravelHeaderActions,
                    VelocityHeaderActions: velocityHeaderActions ?? context.VelocityHeaderActions,
                    ImuHeaderActions: imuHeaderActions ?? context.ImuHeaderActions,
                    PitchRollHeaderActions: pitchRollHeaderActions ?? context.PitchRollHeaderActions,
                    SpeedHeaderActions: speedHeaderActions ?? context.SpeedHeaderActions,
                    ElevationHeaderActions: elevationHeaderActions ?? context.ElevationHeaderActions),
                Analysis: new RecordedAnalysisPresentationState(
                    FrontAnalysis: context.FrontAnalysisState,
                    RearAnalysis: context.RearAnalysisState,
                    CompressionBalance: context.CompressionBalanceState,
                    ReboundBalance: context.ReboundBalanceState,
                    FrontForkVibration: context.FrontForkVibrationState,
                    FrontFrameVibration: context.FrontFrameVibrationState,
                    RearForkVibration: context.RearForkVibrationState,
                    RearFrameVibration: context.RearFrameVibrationState),
                DampingPercentages: context.DampingPercentages,
                PlotDampingSpeedCutoffs: context.PlotDampingSpeedCutoffs,
                CanEditDampingSpeedCutoffs: context.CanEditDampingSpeedCutoffs,
                SessionInsights: context.SessionInsights,
                SignalPlotContextMenuActionsBySignalRowId: context.SignalPlotContextMenuActionsBySignalRowId,
                ScreenState: context.ScreenState,
                OperationState: context.SessionOperationState),
            AnalysisSelection: analysisSelection ?? new AnalysisSelectionState(
                ActiveFront: context.ActiveFrontAnalysisSelection,
                ActiveRear: context.ActiveRearAnalysisSelection,
                HighlightRanges: context.AnalysisSelectionHighlightRanges));
    }
}
