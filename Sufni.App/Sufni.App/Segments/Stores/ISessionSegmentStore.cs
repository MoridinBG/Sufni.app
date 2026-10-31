using System;
using System.Collections.Generic;
using DynamicData;

namespace Sufni.App.Segments.Stores;

/// <summary>
/// Read-only view of the live source-owned segments. Writes go through
/// <see cref="ISessionSegmentStoreWriter"/>, reserved for coordinators and the
/// composition root.
/// </summary>
public interface ISessionSegmentStore
{
    IObservable<IChangeSet<SessionSegmentSnapshot, Guid>> Connect();

    SessionSegmentSnapshot? Get(Guid segmentId);

    IReadOnlyList<SessionSegmentSnapshot> GetForOwner(string ownerKey);
}
