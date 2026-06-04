using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public interface IRecordedSessionOperationLease : IAsyncDisposable
{
    CancellationToken CancellationToken { get; }
    bool IsCurrent { get; }
    void Report(string message, double percent);
    void Complete();
}
