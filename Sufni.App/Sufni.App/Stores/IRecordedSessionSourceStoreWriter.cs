using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Stores;

public interface IRecordedSessionSourceStoreWriter : IRecordedSessionSourceStore
{
    Task SaveAsync(RecordedSessionSource source, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid sessionId, CancellationToken cancellationToken = default);
    void Upsert(RecordedSessionSourceSnapshot snapshot);
    void Remove(Guid sessionId);
}
