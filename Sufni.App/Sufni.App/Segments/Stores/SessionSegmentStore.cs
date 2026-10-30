using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Shared.Stores;
using Sufni.App.Shared.Base;

using Sufni.App.Segments.Models;
namespace Sufni.App.Segments.Stores;

internal sealed class SessionSegmentStore(
    StorePublication publication)
    : SourceCacheStoreBase<SessionSegmentSnapshot, Guid>(snapshot => snapshot.Id, publication), ISessionSegmentStoreWriter
{
    public IReadOnlyList<SessionSegmentSnapshot> GetForSession(Guid sessionId) =>
        Items.Where(segment => segment.SessionId == sessionId).ToList();

    public async Task PublishSegmentsAsync(IReadOnlyCollection<SessionSegment> segments)
    {
        await PublishRemovalsAsync(segments.Where(segment => segment.Deleted is not null).Select(segment => segment.Id));
        await PublishSnapshotsAsync(segments.Where(segment => segment.Deleted is null).Select(SessionSegmentSnapshot.From));
    }
}
