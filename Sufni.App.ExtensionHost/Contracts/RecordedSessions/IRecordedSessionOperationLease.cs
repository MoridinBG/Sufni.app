using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public interface IRecordedSessionOperationLease : IAsyncDisposable
{
    CancellationToken CancellationToken { get; }
    bool IsCurrent { get; }

    /// <summary>
    /// Reports progress for the active recorded-session operation. Percent is interpreted on a 0..100 scale.
    /// </summary>
    void Report(string message, double percent);

    void Complete();
}
