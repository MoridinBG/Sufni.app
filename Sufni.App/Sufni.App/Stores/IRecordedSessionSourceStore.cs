using System;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Models;

namespace Sufni.App.Stores;

/// <summary>
/// Read side of the recorded-session source cache.
/// It exposes lightweight source metadata reactively while keeping full raw
/// source payload loading as an explicit operation.
/// </summary>
public interface IRecordedSessionSourceStore
{
    IObservable<IChangeSet<RecordedSessionSourceSnapshot, Guid>> Connect();
    RecordedSessionSourceSnapshot? Get(Guid sessionId);
    Task<RecordedSessionSource?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task RefreshAsync();
}
