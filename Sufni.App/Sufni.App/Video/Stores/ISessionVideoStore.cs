using System;
using DynamicData;

namespace Sufni.App.Video.Stores;

/// <summary>
/// Read-only view of the linked video clips of all sessions. Writes go
/// through <see cref="ISessionVideoStoreWriter"/>, reserved for the video
/// coordinator and the composition root.
/// </summary>
public interface ISessionVideoStore
{
    IObservable<IChangeSet<SessionVideoSnapshot, Guid>> Connect();

    SessionVideoSnapshot? Get(Guid clipId);
}
