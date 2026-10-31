using System.Collections.Generic;
using System;
using DynamicData;

namespace Sufni.App.Video.Stores;

/// <summary>
/// Read-only view of the source-owned video clips. Writes go
/// through <see cref="ISessionVideoStoreWriter"/>, reserved for the video
/// coordinator and the composition root.
/// </summary>
public interface ISessionVideoStore
{
    IObservable<IChangeSet<SessionVideoSnapshot, Guid>> Connect();

    SessionVideoSnapshot? Get(Guid clipId);

    IReadOnlyList<SessionVideoSnapshot> GetAll();
}
