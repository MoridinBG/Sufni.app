using System;

namespace Sufni.App.SessionGraph;

public interface IRecordedSessionDomainQuery
{
    RecordedSessionDomainSnapshot? Get(Guid sessionId);
}
