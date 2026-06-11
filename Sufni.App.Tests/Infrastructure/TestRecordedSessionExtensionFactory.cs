using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

namespace Sufni.App.Tests.Infrastructure;

internal sealed class TestRecordedSessionExtensionFactory(
    string extensionId,
    Action<TestRecordedSessionExtensionScope>? configureScope = null) : IRecordedSessionExtensionFactory
{
    public string ExtensionId { get; } = extensionId;
    public RecordedSessionHostContext? Context { get; private set; }
    public TestRecordedSessionExtensionScope? Scope { get; private set; }
    public List<TestRecordedSessionExtensionScope> Scopes { get; } = [];

    public IRecordedSessionExtensionScope Create(RecordedSessionHostContext context)
    {
        Context = context;
        Scope = new TestRecordedSessionExtensionScope();
        Scopes.Add(Scope);
        configureScope?.Invoke(Scope);
        return Scope;
    }
}

internal sealed class TestRecordedSessionExtensionScope : IRecordedSessionExtensionScope
{
    public bool Initialized { get; private set; }
    public bool Disposed { get; private set; }
    public RecordedSessionExtensionSlots Slots { get; } = new();
    public List<RecordedSessionHostState> UpdatedStates { get; } = [];

    public ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        Initialized = true;
        return ValueTask.CompletedTask;
    }

    public void UpdateHostState(RecordedSessionHostState state)
    {
        UpdatedStates.Add(state);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
