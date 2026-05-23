using Sufni.App.Services;

namespace Sufni.App.Tests.Infrastructure;

public sealed class RecordingUiThreadDispatcher : IUiThreadDispatcher
{
    public int PostCount { get; private set; }
    public int InvokeCount { get; private set; }

    public bool CheckAccess() => true;

    public void Post(Action action)
    {
        PostCount++;
        action();
    }

    public Task InvokeAsync(Action action)
    {
        InvokeCount++;
        action();
        return Task.CompletedTask;
    }

    public async Task InvokeAsync(Func<Task> action)
    {
        InvokeCount++;
        await action();
    }

    public Task<T> InvokeAsync<T>(Func<T> action)
    {
        InvokeCount++;
        return Task.FromResult(action());
    }
}
