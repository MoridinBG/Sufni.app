using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public interface IRecordedSessionExtensionScope : IAsyncDisposable
{
    ValueTask InitializeAsync(CancellationToken cancellationToken);
    void UpdateHostState(RecordedSessionHostState state);
}
