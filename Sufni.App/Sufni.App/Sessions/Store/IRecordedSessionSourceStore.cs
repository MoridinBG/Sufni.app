using System;
using DynamicData;

namespace Sufni.App.Sessions.Store;

/// <summary>
/// Read side of the recorded-session source cache.
/// Exposes lightweight source metadata without loading raw payloads.
/// </summary>
public interface IRecordedSessionSourceStore
{
    IObservable<IChangeSet<RecordedSessionSourceSnapshot, Guid>> Connect();
    RecordedSessionSourceSnapshot? Get(Guid sessionId);
}
