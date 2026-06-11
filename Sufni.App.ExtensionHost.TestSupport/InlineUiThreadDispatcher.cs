using Sufni.App.ExtensionHost.Contracts.Services;

namespace Sufni.App.ExtensionHost.TestSupport;

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
