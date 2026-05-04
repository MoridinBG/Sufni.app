using System;
using DynamicData;

namespace Sufni.App.SessionGraph;

public interface IRecordedSessionGraph
{
    IObservable<IChangeSet<RecordedSessionSummary, Guid>> ConnectSessions();
    IObservable<RecordedSessionDomainSnapshot> WatchSession(Guid sessionId);
}
