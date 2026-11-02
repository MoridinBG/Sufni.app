using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;
using System.Reactive.Linq;
using Sufni.App.Tests.TestSupport.Async;
using NSubstitute;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Store;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Video.Models;
using Sufni.App.Video.Stores;

namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionEditingCoordinatorTests
{
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly ISessionPersistenceTransactionRunner transactions = Substitute.For<ISessionPersistenceTransactionRunner>();
    private readonly ISessionRecomputeEngine recomputeEngine = Substitute.For<ISessionRecomputeEngine>();
    private readonly SessionSnapshot session = TestSnapshots.Session(id: Guid.NewGuid());

    public SessionEditingCoordinatorTests()
    {
        sessionStore.Get(session.Id).Returns(session);
        sessionRepository.GetSessionAsync(session.Id).Returns(new Session(session.Id, session.Name, "", null));
        recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>())
            .Returns(Task.FromResult<SessionRecomputeResult>(new SessionRecomputeResult.Recomputed()));
    }

    [Fact]
    public async Task SplitAsync_PublishesAndRecomputesBothParts_AndReportsSecondPart()
    {
        var part2 = TestSnapshots.Session(id: Guid.NewGuid());
        sessionStore.Get(part2.Id).Returns(part2);
        sessionRepository.GetSessionAsync(part2.Id).Returns(new Session(part2.Id, part2.Name, "", null));
        transactions.SplitAsync(session, 3).Returns(new SessionEditCommit.Committed([session.Id, part2.Id], part2.Id));

        var completion = new TaskCompletionSource<SessionRecomputeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>())
            .Returns(completion.Task);
        var progress = new RecordingProgress<SessionEditProgress>();
        var operation = CreateCoordinator().SplitAsync(session.Id, 3, progress);

        Assert.Equal(part2.Id, Assert.Single(progress.Reports).CreatedSessionId);
        Assert.False(operation.IsCompleted);
        completion.SetResult(new SessionRecomputeResult.Recomputed());
        var result = await operation.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new SessionEditResult.Applied(), result);
        await sessionStore.Received(1).PublishSessionsChangedAsync(
            Arg.Is<IReadOnlyCollection<SessionSnapshot>>(ids => ids.Select(snapshot => snapshot.Id).SequenceEqual(new[] { session.Id, part2.Id })),
            Arg.Any<CancellationToken>());
        await recomputeEngine.Received(1).RequestRecomputeAsync(session.Id);
        await recomputeEngine.Received(1).RequestRecomputeAsync(part2.Id);
        Assert.Equal(part2.Id, Assert.Single(progress.Reports).CreatedSessionId);
    }

    [Fact]
    public async Task TrimUntilEndAsync_WhenCommitIsRejected_FailsWithoutPublishing()
    {
        transactions.TrimUntilEndAsync(session, 4).Returns(new SessionEditCommit.Rejected("Session changed while editing."));

        var result = await CreateCoordinator().TrimUntilEndAsync(session.Id, 4);

        Assert.Equal(SessionEditFailureReason.Rejected, Assert.IsType<SessionEditResult.Failed>(result).Reason);
        await sessionStore.DidNotReceive().PublishSessionsChangedAsync(
            Arg.Any<IReadOnlyCollection<SessionSnapshot>>(),
            Arg.Any<CancellationToken>());
        await recomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task UndoTrimAsync_WhenRecomputeFails_AppliesWithRecomputeError()
    {
        transactions.UndoTrimAsync(session).Returns(new SessionEditCommit.Committed([session.Id]));
        recomputeEngine.RequestRecomputeAsync(session.Id)
            .Returns(Task.FromResult<SessionRecomputeResult>(new SessionRecomputeResult.Failed(SessionRecomputeFailureReason.Recompute, "source is corrupt")));

        var result = await CreateCoordinator().UndoTrimAsync(session.Id);

        Assert.NotNull(Assert.IsType<SessionEditResult.Applied>(result).RecomputeFailure);
    }

    [Fact]
    public async Task TrimFromBeginningAsync_WhenSessionIsMissing_Fails()
    {
        var result = await CreateCoordinator().TrimFromBeginningAsync(Guid.NewGuid(), 2);

        Assert.Equal(new SessionEditResult.Missing(), result);
    }

    [Fact]
    public async Task SplitAsync_PublishesRemovedAndChangedRowsInOneCommit()
    {
        var dispatcher = new InlineUiThreadDispatcher();
        using var publication = new StorePublication(dispatcher);
        using var store = new SessionStore(publication);
        await store.PublishSessionsChangedAsync([session]);
        var secondId = Guid.NewGuid();
        transactions.SplitAsync(session, 3).Returns(new SessionEditCommit.Committed([session.Id, secondId], secondId));
        sessionRepository.GetSessionAsync(session.Id).Returns((Session?)null);
        sessionRepository.GetSessionAsync(secondId).Returns(new Session(secondId, "Second", "", null));
        var observed = new List<IReadOnlyList<SessionSnapshot>>();
        using var subscription = publication.Committed.Subscribe(_ => observed.Add(store.GetAll()));
        var coordinator = new SessionEditingCoordinator(store, transactions, recomputeEngine, sessionRepository,
            new InlineBackgroundTaskRunner(), dispatcher, publication);

        var result = await coordinator.SplitAsync(session.Id, 3);

        Assert.IsType<SessionEditResult.Applied>(result);
        Assert.Equal(secondId, Assert.Single(Assert.Single(observed)).Id);
    }

    private SessionEditingCoordinator CreateCoordinator() =>
        new(sessionStore, transactions, recomputeEngine, sessionRepository, new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(), new StorePublication(new InlineUiThreadDispatcher()));
}
