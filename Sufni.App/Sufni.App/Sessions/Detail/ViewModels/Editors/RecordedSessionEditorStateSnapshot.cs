using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Presentation;
using Sufni.Telemetry;

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
        RecordedSignalToggleState? signalToggles = null,
        RecordedAnalysisModeState? analysisModes = null,
        AnalysisSelectionState? analysisSelection = null,
        SessionScreenPresentationState? screenState = null,
        SessionOperationPresentationState? operationState = null,
        SessionDampingPercentages? dampingPercentages = null,
        IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>? signalPlotContextMenuActionsBySignalRowId = null,
        SessionInsightsResult? sessionInsights = null,
        RecordedSignalSurfaceState? signalSurfaces = null)
    {
        var toggles = signalToggles ?? RecordedSignalToggleState.From(context);
        var modes = analysisModes ?? RecordedAnalysisModeState.From(context);
        var surfaces = signalSurfaces ?? RecordedSignalSurfaceState.From(context);

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
                SelectedTravelDistributionMode: modes.SelectedTravelDistributionMode,
                SelectedBalanceDisplacementMode: modes.SelectedBalanceDisplacementMode,
                SelectedBalanceSpeedMode: modes.SelectedBalanceSpeedMode,
                SelectedVelocityAverageMode: modes.SelectedVelocityAverageMode,
                SelectedSessionInsightsTargetProfile: modes.SelectedSessionInsightsTargetProfile,
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
                    Travel: surfaces.Travel,
                    Velocity: surfaces.Velocity,
                    Imu: surfaces.Imu,
                    PitchRoll: surfaces.PitchRoll,
                    Speed: surfaces.Speed,
                    Elevation: surfaces.Elevation,
                    ShowAirtime: toggles.ShowAirtime,
                    ShowVelocityAirtime: toggles.ShowVelocityAirtime,
                    ShowImuAirtime: toggles.ShowImuAirtime,
                    ShowPitchRollAirtime: toggles.ShowPitchRollAirtime,
                    ShowSpeedAirtime: toggles.ShowSpeedAirtime,
                    ShowElevationAirtime: toggles.ShowElevationAirtime,
                    ShowAnalysisSelection: toggles.ShowAnalysisSelection,
                    ShowVelocityAnalysisSelection: toggles.ShowVelocityAnalysisSelection,
                    ShowImuAnalysisSelection: toggles.ShowImuAnalysisSelection,
                    ShowPitchRollAnalysisSelection: toggles.ShowPitchRollAnalysisSelection,
                    ShowSpeedAnalysisSelection: toggles.ShowSpeedAnalysisSelection,
                    ShowElevationAnalysisSelection: toggles.ShowElevationAnalysisSelection,
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
                DampingPercentages: dampingPercentages ?? context.DampingPercentages,
                PlotDampingSpeedCutoffs: context.PlotDampingSpeedCutoffs,
                CanEditDampingSpeedCutoffs: context.CanEditDampingSpeedCutoffs,
                SessionInsights: sessionInsights ?? context.SessionInsights,
                SignalPlotContextMenuActionsBySignalRowId: signalPlotContextMenuActionsBySignalRowId ?? context.SignalPlotContextMenuActionsBySignalRowId,
                ScreenState: screenState ?? context.ScreenState,
                OperationState: operationState ?? context.SessionOperationState),
            AnalysisSelection: analysisSelection ?? new AnalysisSelectionState(
                ActiveFront: context.ActiveFrontAnalysisSelection,
                ActiveRear: context.ActiveRearAnalysisSelection,
                HighlightRanges: context.AnalysisSelectionHighlightRanges));
    }
}

internal sealed record RecordedAnalysisModeState(
    TravelDistributionMode SelectedTravelDistributionMode,
    BalanceDisplacementMode SelectedBalanceDisplacementMode,
    BalanceSpeedMode SelectedBalanceSpeedMode,
    VelocityAverageMode SelectedVelocityAverageMode,
    SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile)
{
    public static RecordedAnalysisModeState From(RecordedSessionContext context)
    {
        return new RecordedAnalysisModeState(
            context.SelectedTravelDistributionMode,
            context.SelectedBalanceDisplacementMode,
            context.SelectedBalanceSpeedMode,
            context.SelectedVelocityAverageMode,
            context.SelectedSessionInsightsTargetProfile);
    }
}

internal sealed record RecordedSignalSurfaceState(
    SurfacePresentationState Travel,
    SurfacePresentationState Velocity,
    SurfacePresentationState Imu,
    SurfacePresentationState PitchRoll,
    SurfacePresentationState Speed,
    SurfacePresentationState Elevation)
{
    public static RecordedSignalSurfaceState From(RecordedSessionContext context)
    {
        return new RecordedSignalSurfaceState(
            context.TravelSignalState,
            context.VelocitySignalState,
            context.ImuSignalState,
            context.PitchRollSignalState,
            context.SpeedSignalState,
            context.ElevationSignalState);
    }
}

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
    bool ShowElevationAnalysisSelection)
{
    public static RecordedSignalToggleState From(RecordedSessionContext context)
    {
        return new RecordedSignalToggleState(
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
            ShowElevationAnalysisSelection: context.ShowElevationAnalysisSelection);
    }
}
