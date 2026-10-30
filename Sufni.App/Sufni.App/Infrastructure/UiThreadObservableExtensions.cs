using System;
using System.Reactive.Linq;
using System.Threading;

namespace Sufni.App.Infrastructure;

public static class UiThreadObservableExtensions
{
    public static IObservable<T> ObserveOnUiThread<T>(
        this IObservable<T> source,
        IUiThreadDispatcher dispatcher) =>
        source.ObserveOn(new DispatcherContext(dispatcher));

    private sealed class DispatcherContext(IUiThreadDispatcher dispatcher) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) =>
            dispatcher.Post(() => callback(state));
    }
}
