using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Segments.Models;
namespace Sufni.App.Segments.Stores;

public interface ISessionSegmentStoreWriter : ISessionSegmentStore
{
    Task ReplaceAllAsync(IReadOnlyList<SessionSegmentSnapshot> snapshots);

    /// <summary>
    /// Publishes written rows; deleted rows leave the store.
    /// </summary>
    Task PublishSegmentsAsync(IReadOnlyCollection<SessionSegment> segments);
}
