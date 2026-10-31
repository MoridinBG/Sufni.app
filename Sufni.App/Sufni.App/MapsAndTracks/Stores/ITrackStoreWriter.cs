using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sufni.App.MapsAndTracks.Stores;

public interface ITrackStoreWriter : ITrackStore
{
    Task ReplaceAllAsync(IReadOnlyList<TrackSnapshot> snapshots);
    Task PublishTracksChangedAsync(IReadOnlyCollection<TrackSnapshot> snapshots);
    Task PublishTracksRemovedAsync(IReadOnlyCollection<Guid> ids);
}
