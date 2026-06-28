using System.Collections.Generic;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;
using Sufni.App.ExtensionHost.TestSupport;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.SessionGraph;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services;

// Coverage for the one-time, per-device processing-option reset normalization.
// It resets each source-backed session's stored velocity-filter option to the
// 25 ms default, recomputes through the engine, and records a core_migration
// marker exactly once — only after a full, successful pass. The marker is read
// back through a real SQLite connection so the run-once / resumable contract is
// asserted against actual persistence, not a substitute.
public class ProcessingOptionsResetMigrationTests
{
    private const string MigrationId = "processing_options_normalize_v3_202606";

    private readonly IRecordedSessionSourceRepository sourceRepository = Substitute.For<IRecordedSessionSourceRepository>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly IAppDataRefresher appDataRefresher = Substitute.For<IAppDataRefresher>();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>();
    private readonly IRecordedSessionProcessingOptionCache optionCache = Substitute.For<IRecordedSessionProcessingOptionCache>();
    private readonly ISessionRecomputeEngine recomputeEngine = Substitute.For<ISessionRecomputeEngine>();

    private ProcessingOptionsResetMigration CreateMigration(SqliteConnectionContext context) => new(
        context,
        sourceRepository,
        sessionRepository,
        appDataRefresher,
        sessionPreferences,
        optionCache,
        recomputeEngine,
        new InlineBackgroundTaskRunner());

    private static Session SessionWithId(Guid id) => new(id, "session", string.Empty, Guid.NewGuid());

    private static IReadOnlyDictionary<Guid, SessionPreferences> Preferences(params (Guid Id, int Window)[] entries)
    {
        var map = new Dictionary<Guid, SessionPreferences>();
        foreach (var (id, window) in entries)
        {
            map[id] = new SessionPreferences { Processing = new SessionProcessingPreferences(window) };
        }

        return map;
    }

    private static async Task<bool> MarkerAppliedAsync(SqliteConnectionContext context)
    {
        var connection = await context.GetInitializedConnectionAsync();
        return await new CoreMigrationStore(connection).IsAppliedAsync(MigrationId);
    }

    [Fact]
    public async Task RunAsync_ResetsNonDefaultSourceBackedSessions_RecomputesAll_AndMarksApplied()
    {
        using var tempDatabase = new TempDatabase("processing-option-reset.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);

        var nonDefault = Guid.NewGuid();     // source-backed, 100 ms -> reset to 25
        var alreadyDefault = Guid.NewGuid(); // source-backed, 25 ms -> recompute only
        var sourceless = Guid.NewGuid();     // no raw source -> untouched

        sessionRepository.GetSessionsAsync().Returns(new List<Session>
        {
            SessionWithId(nonDefault),
            SessionWithId(alreadyDefault),
            SessionWithId(sourceless)
        });
        sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid> { nonDefault, alreadyDefault });
        sessionPreferences.GetAllRecordedAsync().Returns(Preferences((nonDefault, 100), (alreadyDefault, 25)));
        recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>())
            .Returns(new SessionRecomputeResult.Recomputed(1));

        await CreateMigration(context).RunAsync();

        // Only the non-default source-backed session has its stored option reset;
        // an already-default one needs no write and a source-less one is not a target.
        await sessionPreferences.Received(1).ResetRecordedProcessingToDefaultLocallyAsync(nonDefault);
        await sessionPreferences.DidNotReceive().ResetRecordedProcessingToDefaultLocallyAsync(alreadyDefault);
        await sessionPreferences.DidNotReceive().ResetRecordedProcessingToDefaultLocallyAsync(sourceless);

        // Both source-backed sessions are re-aligned to 25 ms in the option cache and recomputed.
        optionCache.Received(1).Set(nonDefault, Arg.Is<TelemetryProcessingOptions>(o => o.VelocityFilterWindowMilliseconds == 25));
        optionCache.Received(1).Set(alreadyDefault, Arg.Is<TelemetryProcessingOptions>(o => o.VelocityFilterWindowMilliseconds == 25));
        optionCache.DidNotReceive().Set(sourceless, Arg.Any<TelemetryProcessingOptions>());
        await recomputeEngine.Received(1).RequestRecomputeAsync(nonDefault, RecomputeReason.Migration);
        await recomputeEngine.Received(1).RequestRecomputeAsync(alreadyDefault, RecomputeReason.Migration);
        await recomputeEngine.DidNotReceive().RequestRecomputeAsync(sourceless, Arg.Any<RecomputeReason>());

        // Stores are populated before the engine reads them; the marker is set on success.
        await appDataRefresher.Received(1).RefreshAsync();
        Assert.True(await MarkerAppliedAsync(context));
    }

    [Fact]
    public async Task RunAsync_LeavesMarkerUnwritten_WhenARecomputeFails()
    {
        using var tempDatabase = new TempDatabase("processing-option-reset-failure.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);
        var sessionId = Guid.NewGuid();

        sessionRepository.GetSessionsAsync().Returns(new List<Session> { SessionWithId(sessionId) });
        sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid> { sessionId });
        sessionPreferences.GetAllRecordedAsync().Returns(Preferences((sessionId, 100)));
        recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>())
            .Returns<SessionRecomputeResult>(_ => throw new InvalidOperationException("recompute failed"));

        // The pass is fire-and-forget safe (never throws) but must not mark itself
        // applied on failure, so the next launch retries.
        await CreateMigration(context).RunAsync();

        Assert.False(await MarkerAppliedAsync(context));
    }

    [Fact]
    public async Task RunAsync_LeavesMarkerUnwritten_WhenRecomputeReturnsFailureResult()
    {
        using var tempDatabase = new TempDatabase("processing-option-reset-returned-failure.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);
        var sessionId = Guid.NewGuid();

        sessionRepository.GetSessionsAsync().Returns(new List<Session> { SessionWithId(sessionId) });
        sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid> { sessionId });
        sessionPreferences.GetAllRecordedAsync().Returns(Preferences((sessionId, 100)));
        recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>())
            .Returns(new SessionRecomputeResult.Failed("bad source"));

        await CreateMigration(context).RunAsync();

        Assert.False(await MarkerAppliedAsync(context));
    }

    [Fact]
    public async Task RunAsync_TreatsAlreadyCurrentSessionAsSuccessfulNoOp()
    {
        using var tempDatabase = new TempDatabase("processing-option-reset-current.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);
        var sessionId = Guid.NewGuid();

        sessionRepository.GetSessionsAsync().Returns(new List<Session> { SessionWithId(sessionId) });
        sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid> { sessionId });
        sessionPreferences.GetAllRecordedAsync().Returns(Preferences((sessionId, 25)));
        recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>())
            .Returns(new SessionRecomputeResult.NotRecomputable(new SessionStaleness.Current()));

        await CreateMigration(context).RunAsync();

        Assert.True(await MarkerAppliedAsync(context));
    }

    [Fact]
    public async Task RunAsync_MarksApplied_WhenSourceBackedSessionIsNotRecomputable()
    {
        using var tempDatabase = new TempDatabase("processing-option-reset-not-recomputable.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);
        var sessionId = Guid.NewGuid();

        sessionRepository.GetSessionsAsync().Returns(new List<Session> { SessionWithId(sessionId) });
        sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid> { sessionId });
        sessionPreferences.GetAllRecordedAsync().Returns(Preferences((sessionId, 100)));
        // A source-backed session whose setup/bike was removed cannot be normalized now;
        // it surfaces through the normal not-recomputable staleness UI, so the migration
        // must treat it as done rather than re-run the whole pass every launch forever.
        recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>())
            .Returns(new SessionRecomputeResult.NotRecomputable(
                new SessionStaleness.MissingDependencies(SetupMissing: true, BikeMissing: false)));

        await CreateMigration(context).RunAsync();

        Assert.True(await MarkerAppliedAsync(context));
    }

    [Fact]
    public async Task RunAsync_NoOps_WhenMarkerAlreadyApplied()
    {
        using var tempDatabase = new TempDatabase("processing-option-reset-applied.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);
        var connection = await context.GetInitializedConnectionAsync();
        await new CoreMigrationStore(connection).MarkAppliedAsync(MigrationId);

        await CreateMigration(context).RunAsync();

        // A second startup short-circuits before enumerating or recomputing anything.
        await sessionRepository.DidNotReceive().GetSessionsAsync();
        await appDataRefresher.DidNotReceive().RefreshAsync();
        await recomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
    }

    [Fact]
    public async Task RunAsync_MarksAppliedWithoutRecomputing_WhenLibraryIsEmpty()
    {
        using var tempDatabase = new TempDatabase("processing-option-reset-empty.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);

        sessionRepository.GetSessionsAsync().Returns(new List<Session>());
        sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid>());

        await CreateMigration(context).RunAsync();

        // A genuinely empty library has nothing to migrate now or later, so it is marked done
        // without populating stores or recomputing.
        await appDataRefresher.DidNotReceive().RefreshAsync();
        await recomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
        Assert.True(await MarkerAppliedAsync(context));
    }

    [Fact]
    public async Task RunAsync_DoesNotMark_WhenSessionsExistButNoneAreSourceBacked()
    {
        using var tempDatabase = new TempDatabase("processing-option-reset-nosource.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);

        sessionRepository.GetSessionsAsync().Returns(new List<Session> { SessionWithId(Guid.NewGuid()) });
        sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid>());

        await CreateMigration(context).RunAsync();

        // A non-empty library with no source-backed sessions may be an interrupted/early
        // run, so the marker stays unwritten and the pass retries next launch.
        await appDataRefresher.DidNotReceive().RefreshAsync();
        await recomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
        Assert.False(await MarkerAppliedAsync(context));
    }
}
