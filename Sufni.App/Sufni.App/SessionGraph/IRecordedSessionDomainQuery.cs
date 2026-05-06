using System;

namespace Sufni.App.SessionGraph;

/// <summary>
/// Current-state reader for one recorded-session domain snapshot.
/// The returned snapshot describes the session, processing dependencies,
/// source metadata, fingerprint state, and staleness at the moment of the read.
/// </summary>
public interface IRecordedSessionDomainQuery
{
    RecordedSessionDomainSnapshot? Get(Guid sessionId);
}
