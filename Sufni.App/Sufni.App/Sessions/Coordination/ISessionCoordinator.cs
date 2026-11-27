using Sufni.Telemetry;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
namespace Sufni.App.Sessions.Coordination;

public interface ISessionCoordinator
{
    Task StopAsync();

    event EventHandler<SessionRecomputeAllOutcome>? RecomputeAllFinished;

    Task<SessionStalenessReconciliation> ReconcileStalenessAsync(
        RecordedSessionDomainSnapshot domain, SessionRecomputeChoice choice = SessionRecomputeChoice.Inspect,
        IProgress<SessionRecomputeAllProgress>? progress = null);

    /// <summary>Recomputes one session on the user's request, whether or not it is stale.</summary>
    Task<SessionRecomputeResult> RecomputeAsync(Guid sessionId);

    Task<SessionRecomputeResult> SetProcessingOptionAsync(Guid sessionId, int windowMilliseconds);

    /// <summary>
    /// Decides <paramref name="ranges"/> of a sensor's findings as Drop or Keep. A Drop recomputes the session; the
    /// decision stays saved when the recompute fails.
    /// </summary>
    Task<SensorDecisionResult> SetSensorDecisionAsync(Guid sessionId, SensorIdentity sensor, SensorDecision decision,
        IReadOnlyList<SourceTimeRange> ranges);

    Task<SessionPreferenceLoadResult> LoadPreferencesAsync(Guid sessionId);

    Task<SessionPreferenceSaveResult> SavePreferencesAsync(Guid sessionId, Func<SessionPreferences, SessionPreferences> update);

    Task<GpsOffsetResult> ShiftGpsOffsetAsync(Guid sessionId, double deltaSeconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aligns the GPS clock of each session to its frame gyroscope and publishes the aligned sessions in one step.
    /// </summary>
    Task<GpsAlignAllResult> AlignGpsAsync(IReadOnlyCollection<Guid> sessionIds, IProgress<GpsAlignAllProgress>? progress = null);

    Task<SessionSaveResult> SaveAsync(SessionDraft draft, SessionDraft draftBase);

    Task<SessionDeleteResult> DeleteAsync(Guid sessionId);
}
