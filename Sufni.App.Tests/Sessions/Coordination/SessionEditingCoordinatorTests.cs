using Sufni.App.Tests.TestSupport.Async;
using NSubstitute;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Store;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Video.Models;
using Sufni.App.Video.Stores;

namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionEditingCoordinatorTests
{
    private readonly ISessionStoreWriter sessionStore = Substitute.For<ISessionStoreWriter>();
    private readonly ISessionVideoStoreWriter videoStore = Substitute.For<ISessionVideoStoreWriter>();
    private readonly ISessionSegmentStoreWriter segmentStore = Substitute.For<ISessionSegmentStoreWriter>();
    private readonly ISessionPersistenceTransactionRunner transactions = Substitute.For<ISessionPersistenceTransactionRunner>();
    private readonly ISessionRecomputeEngine recomputeEngine = Substitute.For<ISessionRecomputeEngine>();
    private readonly SessionSnapshot session = TestSnapshots.Session(id: Guid.NewGuid());

    public SessionEditingCoordinatorTests()
    {
        sessionStore.Get(session.Id).Returns(session);
        recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>())
            .Returns(Task.FromResult<SessionRecomputeResult>(new SessionRecomputeResult.Recomputed(1)));
    }

    [Fact]
    public async Task SplitAsync_PublishesAndRecomputesBothParts_AndReportsSecondPart()
    {
        var part2 = TestSnapshots.Session(id: Guid.NewGuid());
        var clip = new SessionVideoClip { Id = Guid.NewGuid(), SessionId = part2.Id };
        sessionStore.Get(part2.Id).Returns(part2);
        transactions.SplitAsync(session, 3).Returns(new SessionEditCommit.Committed([session.Id, part2.Id], [clip], [], part2.Id));

        var completion = new TaskCompletionSource<SessionRecomputeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        recomputeEngine.RequestRecomputeAsync(Arg.Any<Guid>(), RecomputeReason.SourceWindowChanged)
            .Returns(completion.Task);
        var progress = new RecordingProgress<SessionEditProgress>();
        var operation = CreateCoordinator().SplitAsync(session.Id, 3, progress);

        Assert.Equal(part2.Id, Assert.Single(progress.Reports).CreatedSessionId);
        Assert.False(operation.IsCompleted);
        completion.SetResult(new SessionRecomputeResult.Recomputed(1));
        var result = await operation.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new SessionEditResult.Applied(), result);
        await sessionStore.Received(1).PublishSessionsChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { session.Id, part2.Id })),
            Arg.Any<CancellationToken>());
        await videoStore.Received(1).PublishClipsAsync(Arg.Is<IReadOnlyCollection<SessionVideoClip>>(clips => clips.Single() == clip));
        await recomputeEngine.Received(1).RequestRecomputeAsync(session.Id, RecomputeReason.SourceWindowChanged);
        await recomputeEngine.Received(1).RequestRecomputeAsync(part2.Id, RecomputeReason.SourceWindowChanged);
        Assert.Equal(part2.Id, Assert.Single(progress.Reports).CreatedSessionId);
    }

    [Fact]
    public async Task TrimUntilEndAsync_WhenCommitIsRejected_FailsWithoutPublishing()
    {
        transactions.TrimUntilEndAsync(session, 4).Returns(new SessionEditCommit.Rejected("Session changed while editing."));

        var result = await CreateCoordinator().TrimUntilEndAsync(session.Id, 4);

        Assert.Equal(new SessionEditResult.Failed("Session changed while editing."), result);
        await sessionStore.DidNotReceive().PublishSessionsChangedAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
        await recomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
    }

    [Fact]
    public async Task UndoTrimAsync_WhenRecomputeFails_AppliesWithRecomputeError()
    {
        transactions.UndoTrimAsync(session).Returns(new SessionEditCommit.Committed([session.Id], [], []));
        recomputeEngine.RequestRecomputeAsync(session.Id, RecomputeReason.SourceWindowChanged)
            .Returns(Task.FromResult<SessionRecomputeResult>(new SessionRecomputeResult.Failed("source is corrupt")));

        var result = await CreateCoordinator().UndoTrimAsync(session.Id);

        Assert.Equal(new SessionEditResult.Applied("Session could not be recomputed: source is corrupt"), result);
    }

    [Fact]
    public async Task TrimFromBeginningAsync_WhenSessionIsMissing_Fails()
    {
        var result = await CreateCoordinator().TrimFromBeginningAsync(Guid.NewGuid(), 2);

        Assert.Equal(new SessionEditResult.Failed("Session is missing."), result);
    }

    private SessionEditingCoordinator CreateCoordinator() =>
        new(sessionStore, videoStore, segmentStore, transactions, recomputeEngine);
}
