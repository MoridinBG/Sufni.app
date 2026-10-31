using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Stores;

namespace Sufni.App.MapsAndTracks.Stores;

internal sealed class TrackStore(StorePublication publication)
    : SourceCacheStoreBase<TrackSnapshot, Guid>(track => track.Id, publication), ITrackStoreWriter
{
    public IReadOnlyList<TrackSnapshot> GetAll() => Items.ToArray();

    public Task PublishTracksChangedAsync(IReadOnlyCollection<TrackSnapshot> snapshots) => PublishSnapshotsAsync(snapshots);
    public Task PublishTracksRemovedAsync(IReadOnlyCollection<Guid> ids) => PublishRemovalsAsync(ids);
}
