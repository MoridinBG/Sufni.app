using System;
using System.Threading.Tasks;

namespace Sufni.App.Services;

public interface IUiThreadDispatcher
{
    bool CheckAccess();
    void Post(Action action);
    Task InvokeAsync(Action action);
    Task InvokeAsync(Func<Task> action);
    Task<T> InvokeAsync<T>(Func<T> action);
}
