using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.SessionGraph;
using Sufni.Telemetry;

namespace Sufni.App.Services;

// One-time, per-device startup normalization. The processing fingerprint now
// records the velocity-filter option, which marks every pre-existing processed
// session stale on first open. This pass resets each source-backed session's
// stored option to the 25 ms default and recomputes its processed data at 25 ms,
// so the stored fingerprint records the option it was produced with and the
// session is no longer stale. Because the target is a constant, every device
// converges on the same value without depending on sync ordering; a deliberate
// non-default per-session window is reset, by design.
//
// Source-less sessions cannot be recomputed and are left untouched (they surface
// through the normal not-recomputable staleness state). Tracked in core_migration
// so it runs once; an interrupted run leaves the marker unwritten and resumes on
// the next launch.
internal sealed class ProcessingOptionsResetMigration(
    SqliteConnectionContext connectionContext,
    IRecordedSessionSourceRepository recordedSessionSourceRepository,
    ISessionRepository sessionRepository,
    IAppDataRefresher appDataRefresher,
    ISessionPreferences sessionPreferences,
    IRecordedSessionProcessingOptionCache processingOptionCache,
    ISessionRecomputeEngine recomputeEngine,
    IBackgroundTaskRunner backgroundTaskRunner)
{
    private const string MigrationId = "processing_options_normalize_v3_202606";
    private const int DefaultWindow = TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds;
    private static readonly ILogger logger = Log.ForContext<ProcessingOptionsResetMigration>();

    // Runs off the UI thread; safe to call fire-and-forget. Never throws — a
    // failure is logged and leaves the marker unwritten so the next launch retries.
    public Task RunAsync() => backgroundTaskRunner.RunAsync(RunCoreAsync);

    private async Task RunCoreAsync()
    {
        try
        {
            // Awaiting the initialized connection also waits for the startup schema
            // migration that creates the core_migration table.
            var connection = await connectionContext.GetInitializedConnectionAsync();
            var coreMigrations = new CoreMigrationStore(connection);
            if (await coreMigrations.IsAppliedAsync(MigrationId))
            {
                return;
            }

            // Enumerate from the repository (no stores needed yet): the
            // source-backed, non-deleted sessions are the only recomputable ones.
            var nonDeletedSessions = await sessionRepository.GetSessionsAsync();
            var sourceBackedIds = (await recordedSessionSourceRepository.GetSourceBackedSessionIdsAsync())
                .ToHashSet();
            var targets = nonDeletedSessions
                .Select(session => session.Id)
                .Where(sourceBackedIds.Contains)
                .ToList();

            if (targets.Count == 0)
            {
                // A non-empty library with no source-backed sessions might be an
                // interrupted/early run, so leave the marker unwritten and retry
                // next launch rather than mask work. A genuinely empty library has
                // nothing to migrate now or later, so mark it done.
                if (nonDeletedSessions.Count == 0)
                {
                    await coreMigrations.MarkAppliedAsync(MigrationId);
                }

                return;
            }

            // The recompute engine reads the latest store snapshots, so populate the
            // stores (and hydrate the option cache) before recomputing.
            await appDataRefresher.RefreshAsync();

            var storedPreferences = await sessionPreferences.GetAllRecordedAsync();
            var processed = 0;
            foreach (var sessionId in targets)
            {
                // Reset a non-default per-session window to 25 ms, local-only so the
                // synced preferences clock is not bumped. Sessions already at 25 ms
                // (or absent from the document) need no write.
                if (storedPreferences.TryGetValue(sessionId, out var preferences) &&
                    preferences.Processing.VelocityFilterWindowMilliseconds != DefaultWindow)
                {
                    await sessionPreferences.ResetRecordedProcessingToDefaultLocallyAsync(sessionId);
                }

                // Keep the synchronously-read option cache coherent with the reset so
                // the graph compares the recomputed fingerprint against 25 ms.
                processingOptionCache.Set(sessionId, TelemetryProcessingOptions.Default);

                // Drive the recompute through the engine; it serializes per session,
                // recomputes stale sessions, and no-ops anything already current.
                var result = await recomputeEngine.RequestRecomputeAsync(sessionId, RecomputeReason.Migration);
                if (!IsSuccessfulMigrationResult(result))
                {
                    throw new InvalidOperationException(
                        $"Processing-option reset recompute for session {sessionId} did not complete: {DescribeResult(result)}.");
                }

                processed++;
                logger.Verbose(
                    "Processing-option reset {Processed}/{Total} for {SessionId}: {Result}",
                    processed,
                    targets.Count,
                    sessionId,
                    result.GetType().Name);
            }

            await coreMigrations.MarkAppliedAsync(MigrationId);
            logger.Information(
                "Processing-option reset migration normalized {Count} source-backed session(s) to {Window} ms",
                targets.Count,
                DefaultWindow);
        }
        catch (Exception ex)
        {
            // Leave the marker unwritten so the next launch retries.
            logger.Error(ex, "Processing-option reset migration failed; will retry on next launch");
        }
    }

    // A target is done once it recomputes, or once the engine reports it is not
    // recomputable: a not-recomputable session (missing setup/bike, or already
    // current) cannot be normalized now and surfaces through the normal staleness
    // UI, so it must not block the marker forever. Failed/Superseded are not matched
    // here, so they fall through to the retry path (marker left unwritten).
    private static bool IsSuccessfulMigrationResult(SessionRecomputeResult result) =>
        result is SessionRecomputeResult.Recomputed or SessionRecomputeResult.NotRecomputable;

    private static string DescribeResult(SessionRecomputeResult result) => result switch
    {
        SessionRecomputeResult.Failed failed => $"{nameof(SessionRecomputeResult.Failed)} ({failed.ErrorMessage})",
        SessionRecomputeResult.NotRecomputable notRecomputable => $"{nameof(SessionRecomputeResult.NotRecomputable)} ({notRecomputable.Reason.GetType().Name})",
        _ => result.GetType().Name,
    };
}
