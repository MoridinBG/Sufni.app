using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Sessions.Coordination;

public interface ISessionPersistenceTransactionRunner
{
    Task DeleteSessionAsync(
        Guid sessionId,
        Guid? fullTrackId,
        bool deleteFullTrack,
        bool deleteSource,
        CancellationToken cancellationToken = default);
}
