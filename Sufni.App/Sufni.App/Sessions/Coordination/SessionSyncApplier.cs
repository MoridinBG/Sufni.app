using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using System;
using System.IO;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Infrastructure;

using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Sessions.Coordination;

public enum SessionDataApplyResult { Applied, Superseded, Missing, InvalidPayload, Failed }

public interface ISessionSyncApplier
{
    Task<SessionDataApplyResult> ApplyUploadedAsync(Guid sessionId, SessionBlobPayload payload);
    Task<SessionDataApplyResult> ApplyDownloadedAsync(SessionSnapshot expected, SessionBlobPayload payload,
        string? targetFingerprint, SessionProcessedGeneration? generation = null);
}

internal sealed class SessionSyncApplier : ISessionSyncApplier
{
    private static readonly ILogger logger = Log.ForContext<SessionSyncApplier>();

    private readonly ISessionRepository sessionRepository;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionTelemetryProcessor telemetryProcessor;
    private readonly ISessionBlobSwapRequestStore swapRequests;
    private readonly IProcessingFingerprintService fingerprintService;

    public SessionSyncApplier(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISessionTelemetryProcessor telemetryProcessor,
        ISessionBlobSwapRequestStore swapRequests,
        IProcessingFingerprintService fingerprintService)
    {
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.backgroundTaskRunner = backgroundTaskRunner;

        this.telemetryProcessor = telemetryProcessor;
        this.swapRequests = swapRequests;
        this.fingerprintService = fingerprintService;
    }

    public Task<SessionDataApplyResult> ApplyUploadedAsync(Guid sessionId, SessionBlobPayload payload) =>
        ApplyTransferAsync(sessionId, async () =>
        {
            var row = await sessionRepository.GetSessionAsync(sessionId);
            if (row is null) return SessionDataApplyResult.Missing;
            var swap = await swapRequests.GetRequestAsync(sessionId);
            var target = swap?.TargetFingerprint ?? row.ProcessingFingerprintJson;
            if (!StringComparer.Ordinal.Equals(payload.Fingerprint, target))
            {
                if (swap is not null) return SessionDataApplyResult.Superseded;
                logger.Warning("Uploaded session data does not match the requested fingerprint for {SessionId}", sessionId);
                return SessionDataApplyResult.InvalidPayload;
            }
            return await CommitTransferAsync(SessionSnapshot.From(row), payload, swap?.TargetGeneration, swap?.TargetFingerprint);
        });

    public Task<SessionDataApplyResult> ApplyDownloadedAsync(SessionSnapshot expected, SessionBlobPayload payload,
        string? targetFingerprint, SessionProcessedGeneration? generation = null) =>
        ApplyTransferAsync(expected.Id, () => StringComparer.Ordinal.Equals(payload.Fingerprint, targetFingerprint)
            ? CommitTransferAsync(expected, payload, generation, pendingSwapFingerprint: null)
            : Task.FromResult(SessionDataApplyResult.Superseded));

    private async Task<SessionDataApplyResult> ApplyTransferAsync(Guid sessionId, Func<Task<SessionDataApplyResult>> operation)
    {
        try
        {
            return await backgroundTaskRunner.RunAsync(operation);
        }
        catch (InvalidDataException exception)
        {
            logger.Warning(exception, "Incoming session data is invalid for {SessionId}", sessionId);
            return SessionDataApplyResult.InvalidPayload;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Error(exception, "Incoming session data could not be applied for {SessionId}", sessionId);
            return SessionDataApplyResult.Failed;
        }
    }

    private async Task<SessionDataApplyResult> CommitTransferAsync(SessionSnapshot expected, SessionBlobPayload payload,
        SessionProcessedGeneration? generation, string? pendingSwapFingerprint)
    {
        var current = await sessionRepository.GetSessionAsync(expected.Id);
        if (current is null) return SessionDataApplyResult.Missing;
        var telemetry = telemetryProcessor.ReadProcessedTelemetryData(payload.Data);
        if (generation is null && current.HasProcessedData) return SessionDataApplyResult.Superseded;
        var inputs = await sessionRepository.GetProcessingInputBundleAsync(expected.Id);
        var decision = ProcessedGenerationAcceptance.DecideProcessedGenerationTarget(
            inputs is null ? null : fingerprintService.CreateCurrentDatabaseInputs(inputs),
            fingerprintService.Parse(current.ProcessingFingerprintJson),
            fingerprintService.Parse(payload.Fingerprint),
            current.HasProcessedData,
            inputs is not null,
            fingerprintService.CurrentSchemaVersion);
        if (decision != ProcessedGenerationDecision.AcceptIncoming) return SessionDataApplyResult.Superseded;
        var guard = SessionProcessedWriteGuard.From(expected, pendingSwapFingerprint) with { ExpectedInputs = inputs };
        bool committed;
        if (generation is not null)
        {
            committed = await sessionRepository.UpdateSessionProcessedGenerationAsync(
                expected.Id, payload.Data, payload.Fingerprint, generation, guard);
        }
        else
        {
            var duration = SessionDuration.Normalize(telemetry.Metadata?.Duration ?? current.DurationSeconds);
            committed = await sessionRepository.UpdateSessionPsstAsync(expected.Id, payload.Data, payload.Fingerprint, duration, guard);
        }
        if (!committed) return SessionDataApplyResult.Superseded;
        var fresh = await sessionRepository.GetSessionAsync(expected.Id);
        if (fresh is null)
        {
            await sessionStore.PublishSessionsRemovedAsync([expected.Id]);
            return SessionDataApplyResult.Superseded;
        }
        await sessionStore.PublishSessionsChangedAsync([SessionSnapshot.From(fresh)]);
        return SessionDataApplyResult.Applied;
    }

}
