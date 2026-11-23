using System.Threading.Channels;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.TestSupport.Async;

/// <summary>Holds background work until a test explicitly executes the next job.</summary>
public sealed class DeferredBackgroundTaskRunner : IBackgroundTaskRunner
{
    private readonly Channel<Func<Task>> pending = Channel.CreateUnbounded<Func<Task>>();

    public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
        RunAsync(async () => { await work(); return true; }, cancellationToken);

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default) =>
        RunAsync(() => Task.FromResult(work()), cancellationToken);

    public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Writer.TryWrite(async () =>
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

    public Task RunNextAsync() => pending.Reader.TryRead(out var work)
        ? work() : throw new InvalidOperationException("No queued background work.");

    public async Task DrainUntilAsync(ManualUiThreadDispatcher dispatcher, Task completion)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        waiting.CancelAfter(TimeSpan.FromSeconds(5));
        while (!completion.IsCompleted)
        {
            waiting.Token.ThrowIfCancellationRequested();
            await dispatcher.DrainAsync();
            while (pending.Reader.TryRead(out var work)) _ = work();
            if (completion.IsCompleted) break;
            using var wake = CancellationTokenSource.CreateLinkedTokenSource(waiting.Token);
            await Task.WhenAny(completion, dispatcher.WaitForWorkAsync(wake.Token),
                pending.Reader.WaitToReadAsync(wake.Token).AsTask());
            await wake.CancelAsync();
        }
        await completion;
    }
}
