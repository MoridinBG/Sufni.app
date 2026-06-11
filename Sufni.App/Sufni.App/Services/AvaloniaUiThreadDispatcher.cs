using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Sufni.App.ExtensionHost.Services;

namespace Sufni.App.Services;

public sealed class AvaloniaUiThreadDispatcher : IUiThreadDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public void Post(Action action, UiDispatchPriority priority) =>
        Dispatcher.UIThread.Post(action, priority == UiDispatchPriority.Background
            ? DispatcherPriority.Background
            : DispatcherPriority.Default);

    public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public Task InvokeAsync(Func<Task> action)
    {
        if (CheckAccess())
        {
            return action();
        }

        return Dispatcher.UIThread.InvokeAsync(action);
    }

    public Task<T> InvokeAsync<T>(Func<T> action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();
}
