using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Shared.Stores;
using Sufni.App.Shared.Base;

namespace Sufni.App.Segments.Stores;

internal sealed class SessionSegmentStore(
    StorePublication publication)
    : SourceCacheStoreBase<SessionSegmentSnapshot, Guid>(snapshot => snapshot.Id, publication), ISessionSegmentStoreWriter
{
    public IReadOnlyList<SessionSegmentSnapshot> GetForOwner(string ownerKey) =>
        Items.Where(segment => segment.OwnerKey == ownerKey).ToList();

    public Task PublishSegmentsAsync(IReadOnlyCollection<SessionSegmentSnapshot> snapshots, IReadOnlyCollection<Guid> removedIds) =>
        PublishChangesAsync(snapshots, removedIds);
}
