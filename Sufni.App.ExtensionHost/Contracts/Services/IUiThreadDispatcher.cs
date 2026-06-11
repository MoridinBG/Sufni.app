using System;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Contracts.Services;

public interface IUiThreadDispatcher
{
    bool CheckAccess();
    void Post(Action action);

    // Default implementation so existing dispatchers and fakes keep working;
    // priority is a scheduling hint, not a behavioral guarantee.
    void Post(Action action, UiDispatchPriority priority) => Post(action);

    Task InvokeAsync(Action action);
    Task InvokeAsync(Func<Task> action);
    Task<T> InvokeAsync<T>(Func<T> action);
}
