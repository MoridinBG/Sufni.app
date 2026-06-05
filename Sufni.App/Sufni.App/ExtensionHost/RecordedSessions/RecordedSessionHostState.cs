using Sufni.App.Models;
using Sufni.App.SessionGraph;
using Sufni.App.SessionDetails;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public sealed record RecordedSessionHostState(
    SessionSnapshot? Session,
    RecordedSessionDomainSnapshot? Domain,
    TelemetryTimeRange? AnalysisRange,
    TrackTimeRange? TrackTimelineContext,
    double? TelemetryDurationSeconds,
    bool IsLoaded,
    bool IsActive,
    SessionTimelineLinkViewModel? Timeline = null,
    SessionDamperPercentages? DamperPercentages = null,
    DampingSpeedCutoffs? DampingSpeedCutoffs = null,
    VelocityAverageMode VelocityAverageMode = VelocityAverageMode.SampleAveraged,
    TravelHistogramMode TravelHistogramMode = TravelHistogramMode.ActiveSuspension);
