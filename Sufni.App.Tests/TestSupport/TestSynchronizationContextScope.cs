using System;
using System.Threading;

namespace Sufni.App.Tests.Infrastructure;

public sealed class TestSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => d(state);

    public override void Send(SendOrPostCallback d, object? state) => d(state);
}

public sealed class TestSynchronizationContextScope : IDisposable
{
    private readonly SynchronizationContext? previousContext;

    public TestSynchronizationContextScope()
    {
        previousContext = SynchronizationContext.Current;
        Context = new TestSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(Context);
    }

    public TestSynchronizationContext Context { get; }

    public void Dispose()
    {
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }
}