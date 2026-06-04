using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public interface IRecordedSessionExtensionScope : IAsyncDisposable
{
    ValueTask InitializeAsync(CancellationToken cancellationToken);
    RecordedSessionExtensionSlots Slots { get; }
    void UpdateHostState(RecordedSessionHostState state);
}
