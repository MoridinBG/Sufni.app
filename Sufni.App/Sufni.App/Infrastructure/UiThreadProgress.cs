using System;

namespace Sufni.App.Infrastructure;

public sealed class UiThreadProgress<T>(IUiThreadDispatcher dispatcher, Action<T> report) : IProgress<T>
{
    public void Report(T value) => dispatcher.Post(() => report(value));
}
