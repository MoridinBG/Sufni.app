using Sufni.App.Terrain.Queries;
using Sufni.App.Terrain.Models;
using Sufni.App.Shared.Models;
using System.Collections.Generic;
using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Common;
using Sufni.Telemetry;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed record RecordedSessionEditorState(
    RecordedSessionDomainSnapshot? Domain,
    RecordedSessionLoadPresentation Load,
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
    public RecordedTerrainState Terrain { get; init; } = new RecordedTerrainState.Unavailable(
        Sufni.App.Terrain.Models.TerrainAvailabilityReason.NoTelemetry);

    /// <summary>The scope the reducer applies to the loaded profile; null without a profile.</summary>
    public TerrainScope? TerrainScope => Terrain is RecordedTerrainState.Loaded loaded
        ? Intent.Scope is AnalysisScope.Terrain chosen ? chosen.Value : Sufni.App.Terrain.Models.TerrainScope.Default(loaded.Profile)
        : null;

    public TerrainSelectionResult? TerrainSelection { get; init; }

    public TelemetryTimeSelection? TimeSelection => Intent.Scope is not AnalysisScope.SegmentComparison && TerrainSelection is { } terrain
        ? terrain.Selection : Intent.AnalysisRange;

    public SessionTrackTimeline? TrackTimeline => TelemetryData is { } telemetry
        ? SessionTrackTimeline.FromSessionWindow(telemetry.Metadata.Timestamp, Session?.GpsOffsetSeconds ?? 0,
            Session?.WindowStartSeconds ?? 0) : null;
    public RecordedProcessingDraft? ProcessingDraft { get; init; }
    public TelemetryProcessingOptions ProcessingOptions => ProcessingDraft is { } draft
        ? new TelemetryProcessingOptions(draft.WindowMilliseconds)
        : Session?.ProcessingOptions ?? new TelemetryProcessingOptions();

    public static RecordedSessionEditorState CreateInitial()
    {
        var preferences = SessionPreferences.Default;
        return new RecordedSessionEditorState(
            Domain: null,
            Load: new RecordedSessionLoadPresentation.Empty(),
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
                DampingSpeedCutoffs: DampingSpeedCutoffs.Default),
            Presentation: new RecordedSessionEditorPresentationState(
                MapState: SurfacePresentationState.Hidden,
                SignalAvailability: RecordedSessionPresentationDeriver.CreateHiddenSignalAvailabilityState(),
                Signals: RecordedSessionPresentationDeriver.CreateHiddenSignalPresentationState() with { ShowAirtime = true },
                Analysis: RecordedSessionPresentationDeriver.CreateHiddenAnalysisPresentationState(),
                DampingPercentages: SessionDampingPercentages.Empty,
                PlotDampingSpeedCutoffs: DampingSpeedCutoffs.Default,
                CanEditDampingSpeedCutoffs: false,
                SessionInsights: SessionInsightsResult.Hidden,
                ScreenState: SessionScreenPresentationState.Ready,
                OperationState: SessionOperationPresentationState.Hidden),
            AnalysisSelection: new AnalysisSelectionState(
                ActiveFront: null,
                ActiveRear: null,
                HighlightRanges: []));
    }

}

internal sealed record RecordedSessionLoadedData(
    SessionSnapshot? Session,
    TelemetryData? TelemetryData,
    IReadOnlyList<TrackPoint>? FullTrackPoints,
    IReadOnlyList<TrackPoint>? TrackPoints,
    TrackTimeRange? TrackTimelineContext);

internal closed record RecordedSessionLoadPresentation
{
    public sealed record Empty(
        SessionSnapshot? Session = null) : RecordedSessionLoadPresentation;

    public sealed record Loading(
        bool MapExpected,
        SessionDetailLoadProgress Progress,
        SessionSnapshot? Session = null) : RecordedSessionLoadPresentation;

    public sealed record Loaded(
        SessionDetailData Data,
        SessionSnapshot? Session = null) : RecordedSessionLoadPresentation;

    public sealed record IncompleteLocalData(
        MissingSessionData Missing,
        SessionSnapshot? Session = null) : RecordedSessionLoadPresentation;

    public sealed record Failed(
        FailurePresentation Failure,
        SessionSnapshot? Session = null) : RecordedSessionLoadPresentation;
}

internal sealed record RecordedSessionEditorIntentState(
    int SelectedPageIndex,
    TelemetryTimeRange? AnalysisRange,
    double? PendingAnalysisRangeBoundary,
    DampingSpeedCutoffs DampingSpeedCutoffs)
{
    public AnalysisScope Scope { get; init; } = new AnalysisScope.Default();
}

internal sealed record RecordedSessionEditorPresentationState(
    SurfacePresentationState MapState,
    RecordedSignalAvailabilityState SignalAvailability,
    RecordedSignalPresentationState Signals,
    RecordedAnalysisPresentationState Analysis,
    SessionDampingPercentages DampingPercentages,
    DampingSpeedCutoffs PlotDampingSpeedCutoffs,
    bool CanEditDampingSpeedCutoffs,
    SessionInsightsResult SessionInsights,
    SessionScreenPresentationState ScreenState,
    SessionOperationPresentationState OperationState);

internal sealed record RecordedSignalAvailabilityState(
    bool Travel,
    bool Velocity,
    bool Imu,
    bool PitchRoll,
    bool Speed,
    bool Elevation)
{
    public bool Terrain { get; init; }
    public bool Roughness { get; init; }
    public bool Grade { get; init; }
}

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
    bool ShowElevationAnalysisSelection)
{
    public bool ShowTerrainAirtime { get; init; }
    public bool ShowTerrainAnalysisSelection { get; init; }
    public bool ShowRoughnessAirtime { get; init; }
    public bool ShowRoughnessAnalysisSelection { get; init; }
    public bool ShowGradeAirtime { get; init; }
    public bool ShowGradeAnalysisSelection { get; init; }
    public bool ShowPedalBobAirtime { get; init; }
    public bool ShowPedalBobAnalysisSelection { get; init; }
    public SurfacePresentationState Terrain { get; init; } = SurfacePresentationState.Hidden;
    public SurfacePresentationState Roughness { get; init; } = SurfacePresentationState.Hidden;
    public SurfacePresentationState Grade { get; init; } = SurfacePresentationState.Hidden;
    public SurfacePresentationState PedalBob { get; init; } = SurfacePresentationState.Hidden;
}

internal sealed record RecordedAnalysisPresentationState(
    SurfacePresentationState FrontAnalysis,
    SurfacePresentationState RearAnalysis,
    SurfacePresentationState CompressionBalance,
    SurfacePresentationState ReboundBalance,
    SurfacePresentationState FrontForkVibration,
    SurfacePresentationState FrontFrameVibration,
    SurfacePresentationState RearForkVibration,
    SurfacePresentationState RearFrameVibration)
{
    public bool FullSessionBalanceAvailable { get; init; }
}

internal sealed record AnalysisSelectionState(
    TelemetryRangeSelection? ActiveFront,
    TelemetryRangeSelection? ActiveRear,
    IReadOnlyList<TelemetryHighlightRange> HighlightRanges);

internal sealed record RecordedProcessingDraft(int WindowMilliseconds)
{
    public bool IsSubmitted { get; init; }
}
