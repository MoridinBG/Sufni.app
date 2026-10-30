using System.Threading.Channels;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.TestSupport.Async;

/// <summary>Queues UI work until a test explicitly drains it.</summary>
public sealed class ManualUiThreadDispatcher : IUiThreadDispatcher
{
    private readonly Channel<Func<Task>> callbacks = Channel.CreateUnbounded<Func<Task>>();
    private bool executing;

    public bool CheckAccess() => executing;

    public void Post(Action action) => callbacks.Writer.TryWrite(() =>
    {
        action();
        return Task.CompletedTask;
    });

    public Task InvokeAsync(Action action) => InvokeAsync(() =>
    {
        action();
        return Task.CompletedTask;
    });

    public Task InvokeAsync(Func<Task> action)
    {
        if (CheckAccess()) return action();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        callbacks.Writer.TryWrite(async () =>
        {
            try
            {
                await action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    public async Task<T> InvokeAsync<T>(Func<T> action)
    {
        T result = default!;
        await InvokeAsync(() => { result = action(); });
        return result;
    }

    public async Task WaitForWorkAsync(CancellationToken cancellationToken) =>
        await callbacks.Reader.WaitToReadAsync(cancellationToken);

    public async Task DrainAsync()
    {
        executing = true;
        try
        {
            while (callbacks.Reader.TryRead(out var callback)) await callback();
        }
        finally
        {
            executing = false;
        }
    }
}
