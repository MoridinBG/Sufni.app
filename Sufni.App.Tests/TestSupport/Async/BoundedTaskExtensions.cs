namespace Sufni.App.Tests.TestSupport.Async;

public static class BoundedTaskExtensions
{
    public static Task AwaitBoundedAsync(this Task task, TimeSpan timeout) =>
        task.WaitAsync(timeout, TestContext.Current.CancellationToken);

    public static Task<T> AwaitBoundedAsync<T>(this Task<T> task, TimeSpan timeout) =>
        task.WaitAsync(timeout, TestContext.Current.CancellationToken);
}
