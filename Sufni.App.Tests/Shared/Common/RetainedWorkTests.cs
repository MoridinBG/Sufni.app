using Sufni.App.Shared.Common;

namespace Sufni.App.Tests.Shared.Common;

public class RetainedWorkTests
{
    [Fact]
    public async Task DrainWaitsOnlyForWorkAlreadyRetained()
    {
        var work = new RetainedWork();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = work.Retain(first.Task);
        var drain = work.DrainAsync();
        _ = work.Retain(second.Task);

        Assert.False(drain.IsCompleted);
        first.SetResult();
        await drain.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        var laterDrain = work.DrainAsync();
        Assert.False(laterDrain.IsCompleted);
        second.SetResult();
        await laterDrain.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DrainCompletesWhenAnOperationFailsOrIsCanceled(bool cancel)
    {
        var work = new RetainedWork();
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = work.Retain(operation.Task);
        var drain = work.DrainAsync();
        if (cancel) operation.SetCanceled();
        else operation.SetException(new IOException());
        await drain.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
    }
}
