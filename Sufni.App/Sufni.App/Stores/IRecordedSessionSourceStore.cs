using System;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Models;

namespace Sufni.App.Stores;

public interface IRecordedSessionSourceStore
{
    IObservable<IChangeSet<RecordedSessionSourceSnapshot, Guid>> Connect();
    RecordedSessionSourceSnapshot? Get(Guid sessionId);
    Task<RecordedSessionSource?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task RefreshAsync();
}
