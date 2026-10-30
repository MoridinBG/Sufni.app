using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sufni.App.Segments.Stores;

public interface ISessionSegmentStoreWriter : ISessionSegmentStore
{
    Task ReplaceAllAsync(IReadOnlyList<SessionSegmentSnapshot> snapshots);

    /// <summary>
    /// Publishes prepared snapshots and removals in one cache update.
    /// </summary>
    Task PublishSegmentsAsync(IReadOnlyCollection<SessionSegmentSnapshot> snapshots, IReadOnlyCollection<Guid> removedIds);
}
