using System.Collections.Generic;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.TestSupport;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Infrastructure;

// Coverage for the one-time, per-device processing-option reset normalization.
// It resets each source-backed session's stored velocity-filter option to the
// 25 ms default, recomputes through the engine, and records a core_migration
// marker exactly once — only after a full, successful pass. The marker is read
// back through a real SQLite connection so the run-once / resumable contract is
// asserted against actual persistence, not a substitute.
public class ProcessingOptionsResetMigrationTests
{
    private const string MigrationId = "processing_options_normalize_v3_202606";

    public enum RecomputeFailureCase
    {
        Throws,
        ReturnsFailureResult,
    }

    public enum MarkerCase
    {
        AlreadyCurrent,
        NotRecomputable,
        EmptyLibrary,
        SessionsWithoutSources,
    }

    private readonly IRecordedSessionSourceRepository sourceRepository = Substitute.For<IRecordedSessionSourceRepository>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly IAppDataRefresher appDataRefresher = Substitute.For<IAppDataRefresher>();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>();
    private readonly ISessionRecomputeEngine recomputeEngine = Substitute.For<ISessionRecomputeEngine>();

    private ProcessingOptionsResetMigration CreateMigration(SqliteConnectionContext context) => new(
        context,
        sourceRepository,
        sessionRepository,
        appDataRefresher,
        sessionPreferences,
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

    private void ConfigureMarkerCase(MarkerCase markerCase, Guid sessionId)
    {
        switch (markerCase)
        {
            case MarkerCase.AlreadyCurrent:
                sessionRepository.GetSessionsAsync().Returns(new List<Session> { SessionWithId(sessionId) });
                sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid> { sessionId });
                sessionPreferences.GetAllRecordedAsync().Returns(Preferences((sessionId, 25)));
                recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>())
                    .Returns(new SessionRecomputeResult.NotRecomputable(new SessionStaleness.Current()));
                break;

            case MarkerCase.NotRecomputable:
                sessionRepository.GetSessionsAsync().Returns(new List<Session> { SessionWithId(sessionId) });
                sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid> { sessionId });
                sessionPreferences.GetAllRecordedAsync().Returns(Preferences((sessionId, 100)));
                recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>())
                    .Returns(new SessionRecomputeResult.NotRecomputable(
                        new SessionStaleness.MissingDependencies(SetupMissing: true, BikeMissing: false)));
                break;

            case MarkerCase.EmptyLibrary:
                sessionRepository.GetSessionsAsync().Returns(new List<Session>());
                sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid>());
                break;

            case MarkerCase.SessionsWithoutSources:
                sessionRepository.GetSessionsAsync().Returns(new List<Session> { SessionWithId(sessionId) });
                sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid>());
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(markerCase), markerCase, null);
        }
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

        // Both source-backed sessions are recomputed; the option cache follows the
        // no-clock preference reset emission instead of being manually mutated here.
        await recomputeEngine.Received(1).RequestRecomputeAsync(nonDefault, RecomputeReason.Migration);
        await recomputeEngine.Received(1).RequestRecomputeAsync(alreadyDefault, RecomputeReason.Migration);
        await recomputeEngine.DidNotReceive().RequestRecomputeAsync(sourceless, Arg.Any<RecomputeReason>());

        // Stores are populated before the engine reads them; the marker is set on success.
        await appDataRefresher.Received(1).RefreshAsync();
        Assert.True(await MarkerAppliedAsync(context));
    }

    [Theory]
    [InlineData(RecomputeFailureCase.Throws)]
    [InlineData(RecomputeFailureCase.ReturnsFailureResult)]
    public async Task RunAsync_LeavesMarkerUnwritten_WhenRecomputeFails(RecomputeFailureCase failureCase)
    {
        using var tempDatabase = new TempDatabase($"processing-option-reset-{failureCase}.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);
        var sessionId = Guid.NewGuid();

        sessionRepository.GetSessionsAsync().Returns(new List<Session> { SessionWithId(sessionId) });
        sourceRepository.GetSourceBackedSessionIdsAsync().Returns(new List<Guid> { sessionId });
        sessionPreferences.GetAllRecordedAsync().Returns(Preferences((sessionId, 100)));
        if (failureCase == RecomputeFailureCase.Throws)
        {
            recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>())
                .Returns<SessionRecomputeResult>(_ => throw new InvalidOperationException("recompute failed"));
        }
        else
        {
            recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>())
                .Returns(new SessionRecomputeResult.Failed("bad source"));
        }

        await CreateMigration(context).RunAsync();

        Assert.False(await MarkerAppliedAsync(context));
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

    [Theory]
    [InlineData(MarkerCase.AlreadyCurrent, true)]
    [InlineData(MarkerCase.NotRecomputable, true)]
    [InlineData(MarkerCase.EmptyLibrary, true)]
    [InlineData(MarkerCase.SessionsWithoutSources, false)]
    public async Task RunAsync_HandlesMarkerCases(MarkerCase markerCase, bool expectedApplied)
    {
        using var tempDatabase = new TempDatabase($"processing-option-reset-{markerCase}.db");
        var context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath, []);
        var sessionId = Guid.NewGuid();

        ConfigureMarkerCase(markerCase, sessionId);

        await CreateMigration(context).RunAsync();

        if (markerCase is MarkerCase.EmptyLibrary or MarkerCase.SessionsWithoutSources)
        {
            await appDataRefresher.DidNotReceive().RefreshAsync();
            await recomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
        }

        Assert.Equal(expectedApplied, await MarkerAppliedAsync(context));
    }
}
