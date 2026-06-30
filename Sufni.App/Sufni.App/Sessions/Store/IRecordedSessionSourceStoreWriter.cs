using System;
using System.Threading.Tasks;

namespace Sufni.App.Sessions.Store;

/// <summary>
/// Write side of the recorded-session source cache.
/// It owns direct metadata updates after the database has already changed.
/// </summary>
public interface IRecordedSessionSourceStoreWriter : IRecordedSessionSourceStore
{
    Task RefreshAsync();
    void Upsert(RecordedSessionSourceSnapshot snapshot);
    void Remove(Guid sessionId);
}
