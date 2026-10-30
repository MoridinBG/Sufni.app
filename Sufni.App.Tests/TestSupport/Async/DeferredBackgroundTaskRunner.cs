using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.TestSupport.Async;

/// <summary>Holds background work until a test explicitly executes the next job.</summary>
public sealed class DeferredBackgroundTaskRunner : IBackgroundTaskRunner
{
    private readonly Queue<Func<Task>> pending = [];

    public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
        RunAsync(async () => { await work(); return true; }, cancellationToken);

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default) =>
        RunAsync(() => Task.FromResult(work()), cancellationToken);

    public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Enqueue(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.SetResult(await work());
            }
            catch (OperationCanceledException)
            {
                completion.SetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    public Task RunNextAsync() => pending.Dequeue()();
}
