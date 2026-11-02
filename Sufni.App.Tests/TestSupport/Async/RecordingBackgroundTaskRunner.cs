using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.TestSupport.Async;

public sealed class RecordingBackgroundTaskRunner : IBackgroundTaskRunner
{
    public int InvocationCount { get; private set; }
    public Exception? Failure { get; set; }

    public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default)
    {
        InvocationCount++;
        if (Failure is { } failure) throw failure;
        return work();
    }

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        InvocationCount++;
        if (Failure is { } failure) throw failure;
        return Task.FromResult(work());
    }

    public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        InvocationCount++;
        if (Failure is { } failure) throw failure;
        return work();
    }
}