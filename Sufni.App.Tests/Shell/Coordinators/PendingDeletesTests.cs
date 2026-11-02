using Sufni.App.Setups.Coordinators;
using Sufni.App.Shared.Models;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.Shell.Coordinators;

public class PendingDeletesTests
{
    [Fact]
    public async Task Expiry_CommitsDeleteAfterFiveSeconds()
    {
        var clock = new FakeTimeProvider();
        var id = Guid.NewGuid();
        var persisted = new HashSet<Guid> { id };
        var sessions = TestCoordinatorSubstitutes.Session();
        sessions.DeleteAsync(id).Returns(_ =>
        {
            persisted.Remove(id);
            return new SessionDeleteResult.Deleted();
        });
        using var owner = TestPendingDeletes.Create(sessions: sessions, timeProvider: clock);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Completed += (_, _) => completed.TrySetResult();
        var key = new PendingDeleteKey.Session(id);
        owner.Schedule(key, "ride");

        clock.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.Contains(id, persisted);
        Assert.True(owner.IsPending(key));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await completed.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(persisted);
        Assert.False(owner.IsPending(key));
    }

    [Fact]
    public async Task Undo_RemovesPendingIntent_AndFinalizationDoesNotDeleteIt()
    {
        var clock = new FakeTimeProvider();
        var key = new PendingDeleteKey.Session(Guid.NewGuid());
        var sessions = TestCoordinatorSubstitutes.Session();
        using var owner = TestPendingDeletes.Create(sessions: sessions, timeProvider: clock);
        owner.Schedule(key, "ride");

        owner.Undo(key);
        clock.Advance(TimeSpan.FromSeconds(5));
        await owner.FinalizeAllAsync();

        Assert.False(owner.IsPending(key));
        await sessions.DidNotReceive().DeleteAsync(key.Id);
    }

    [Fact]
    public async Task FinalizeAll_JoinsAnAlreadyCommittingDelete_AndWaitsForItsResult()
    {
        var key = new PendingDeleteKey.Session(Guid.NewGuid());
        var committed = new TaskCompletionSource<SessionDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = TestCoordinatorSubstitutes.Session();
        sessions.DeleteAsync(key.Id).Returns(committed.Task);
        using var owner = TestPendingDeletes.Create(sessions: sessions);
        owner.Schedule(key, "ride");
        var commit = owner.CommitAsync(key);
        var finalize = owner.FinalizeAllAsync();
        Assert.False(finalize.IsCompleted);
        Assert.True(owner.IsPending(key));

        committed.SetResult(new SessionDeleteResult.Deleted());
        await Task.WhenAll(commit, finalize).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.False(owner.IsPending(key));
        await sessions.Received(1).DeleteAsync(key.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReferencedEntity_RemovesPendingIntentAndReportsFailureWithoutClosingEditor(bool setup)
    {
        var id = Guid.NewGuid();
        PendingDeleteKey key = setup ? new PendingDeleteKey.Setup(id) : new PendingDeleteKey.Bike(id);
        var bikes = TestCoordinatorSubstitutes.Bike();
        var setups = TestCoordinatorSubstitutes.Setup();
        bikes.DeleteAsync(id).Returns(new BikeDeleteResult.InUse());
        setups.DeleteAsync(id).Returns(new SetupDeleteResult.InUse());
        var shell = Substitute.For<IShellCoordinator>();
        using var owner = TestPendingDeletes.Create(bikes: bikes, setups: setups, shell: shell);
        PendingDeleteCompletion? result = null;
        owner.Completed += (_, value) => result = value;
        owner.Schedule(key, "bike");

        await owner.FinalizeAllAsync();

        Assert.False(owner.IsPending(key));
        Assert.NotNull(result?.Error);
        await shell.DidNotReceive().CloseEditorAsync(Arg.Any<EditorKey>());
    }
}
