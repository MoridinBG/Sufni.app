using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services;

public interface IBackgroundTaskRunner
{
    Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default);

    Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default);

    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default);
}