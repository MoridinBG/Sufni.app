using System;
using DynamicData;

namespace Sufni.App.SessionGraph;

/// <summary>
/// Observable read model for recorded sessions.
/// It maintains list-level summaries and replayable per-session domain
/// snapshots, with related changes collapsed into coherent domain emissions.
/// </summary>
public interface IRecordedSessionGraph
{
    IObservable<IChangeSet<RecordedSessionSummary, Guid>> ConnectSessions();
    IObservable<RecordedSessionDomainSnapshot> WatchSession(Guid sessionId);
}
