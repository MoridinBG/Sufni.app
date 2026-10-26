using System;
using System.Collections.Generic;
using DynamicData;

namespace Sufni.App.Segments.Stores;

/// <summary>
/// Read-only view of the live segments of live sessions. Writes go through
/// <see cref="ISessionSegmentStoreWriter"/>, reserved for coordinators and the
/// composition root.
/// </summary>
public interface ISessionSegmentStore
{
    IObservable<IChangeSet<SessionSegmentSnapshot, Guid>> Connect();

    SessionSegmentSnapshot? Get(Guid segmentId);

    IReadOnlyList<SessionSegmentSnapshot> GetForSession(Guid sessionId);
}
