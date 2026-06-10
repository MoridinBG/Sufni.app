using Sufni.App.Services;
using Sufni.App.ExtensionHost.Services;

namespace Sufni.App.Tests.Infrastructure;

public sealed class RecordingUiThreadDispatcher(bool checkAccess = true) : IUiThreadDispatcher
{
    public int PostCount { get; private set; }
    public int InvokeCount { get; private set; }
    private bool invoking;

    public bool CheckAccess() => checkAccess || invoking;

    public void Post(Action action)
    {
        PostCount++;
        action();
    }

    public Task InvokeAsync(Action action)
    {
        InvokeCount++;
        Invoke(action);
        return Task.CompletedTask;
    }

    public async Task InvokeAsync(Func<Task> action)
    {
        InvokeCount++;
        invoking = true;
        try
        {
            await action();
        }
        finally
        {
            invoking = false;
        }
    }

    public Task<T> InvokeAsync<T>(Func<T> action)
    {
        InvokeCount++;
        invoking = true;
        try
        {
            return Task.FromResult(action());
        }
        finally
        {
            invoking = false;
        }
    }

    private void Invoke(Action action)
    {
        invoking = true;
        try
        {
            action();
        }
        finally
        {
            invoking = false;
        }
    }
}
