using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services;

public sealed class BackgroundTaskRunner : IBackgroundTaskRunner
{
    public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
        Task.Run(work, cancellationToken);

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default) =>
        Task.Run(work, cancellationToken);

    public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default) =>
        Task.Run(work, cancellationToken);
}