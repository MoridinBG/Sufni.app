using Sufni.App.Services;

namespace Sufni.App.Tests.Infrastructure;

public sealed class InlineUiThreadDispatcher : IUiThreadDispatcher
{
    public bool CheckAccess() => true;

    public void Post(Action action) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task InvokeAsync(Func<Task> action) => action();

    public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
}
