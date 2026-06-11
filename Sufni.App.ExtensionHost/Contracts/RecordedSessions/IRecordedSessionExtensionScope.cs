using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public interface IRecordedSessionExtensionScope : IAsyncDisposable
{
    ValueTask InitializeAsync(CancellationToken cancellationToken);
    RecordedSessionExtensionSlots Slots { get; }
    void UpdateHostState(RecordedSessionHostState state);
}
