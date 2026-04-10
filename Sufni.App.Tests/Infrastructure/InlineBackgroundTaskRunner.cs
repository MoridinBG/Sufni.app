using Sufni.App.Services;

namespace Sufni.App.Tests.Infrastructure;

public sealed class InlineBackgroundTaskRunner : IBackgroundTaskRunner
{
    public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default) => Task.FromResult(work());

    public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default) => work();
}