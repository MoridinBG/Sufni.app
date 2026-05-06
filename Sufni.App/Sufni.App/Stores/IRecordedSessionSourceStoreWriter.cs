using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Stores;

/// <summary>
/// Write side of the recorded-session source cache.
/// It owns persistence-backed source saves and removals as well as direct
/// metadata updates after the database has already changed.
/// </summary>
public interface IRecordedSessionSourceStoreWriter : IRecordedSessionSourceStore
{
    Task SaveAsync(RecordedSessionSource source, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid sessionId, CancellationToken cancellationToken = default);
    void Upsert(RecordedSessionSourceSnapshot snapshot);
    void Remove(Guid sessionId);
}
