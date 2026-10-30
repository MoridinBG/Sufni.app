using Microsoft.Extensions.Time.Testing;
using Sufni.App.Shared.Base;

namespace Sufni.App.Tests.Shared.Base;

public class PendingDeleteEntryViewModelTests
{
    [Fact]
    public async Task Expiry_RemovesUndoEntryAndFinalizesDelete()
    {
        var time = new FakeTimeProvider();
        var finalized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingDeleteEntryViewModel? removed = null;
        var entry = new PendingDeleteEntryViewModel("ride",
            () => { finalized.TrySetResult(); return Task.CompletedTask; },
            () => throw new InvalidOperationException("No undo was requested"),
            value => removed = value, new InlineUiThreadDispatcher(), new InlineBackgroundTaskRunner(), time);
        entry.StartTimer(5000);

        time.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.False(finalized.Task.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));

        await finalized.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Same(entry, removed);
    }

    [Fact]
    public void Undo_CancelsExpiryAndRestoresItem()
    {
        var time = new FakeTimeProvider();
        var finalized = false;
        var restored = false;
        PendingDeleteEntryViewModel? removed = null;
        var entry = new PendingDeleteEntryViewModel("ride",
            () => { finalized = true; return Task.CompletedTask; },
            () => restored = true, value => removed = value,
            new InlineUiThreadDispatcher(), new InlineBackgroundTaskRunner(), time);
        entry.StartTimer(5000);

        entry.UndoDeleteCommand.Execute(null);
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Same(entry, removed);
        Assert.True(restored);
        Assert.False(finalized);
    }
}
