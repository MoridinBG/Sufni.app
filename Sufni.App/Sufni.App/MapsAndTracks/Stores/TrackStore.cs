using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Stores;

namespace Sufni.App.MapsAndTracks.Stores;

internal sealed class TrackStore(StorePublication publication)
    : SourceCacheStoreBase<TrackSnapshot, Guid>(track => track.Id, publication), ITrackStoreWriter
{
    public Task PublishTracksChangedAsync(IReadOnlyCollection<TrackSnapshot> snapshots) => PublishSnapshotsAsync(snapshots);
    public Task PublishTracksRemovedAsync(IReadOnlyCollection<Guid> ids) => PublishRemovalsAsync(ids);
}
