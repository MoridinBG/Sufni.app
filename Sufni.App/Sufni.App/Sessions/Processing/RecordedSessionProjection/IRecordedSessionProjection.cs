using System;
using DynamicData;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Observable read model for recorded sessions.
/// It maintains list-level summaries and replayable per-session domain
/// snapshots, with related changes collapsed into coherent domain emissions.
/// </summary>
public interface IRecordedSessionProjection
{
    IObservable<IChangeSet<RecordedSessionSummary, Guid>> ConnectSessions();
    IObservable<RecordedSessionDomainSnapshot> WatchSession(Guid sessionId);

    // Enqueues a coalesced re-evaluation of one session. Used by the
    // preference->projection edge so a processing-option change re-runs staleness
    // evaluation for that session even when no session/setup/bike/source store
    // change occurred.
    void QueueRecompute(Guid sessionId);
}
