using System;
using System.Collections.Generic;
using DynamicData;

namespace Sufni.App.MapsAndTracks.Stores;

/// <summary>Shared track metadata; point payloads remain in persistence.</summary>
public interface ITrackStore
{
    IObservable<IChangeSet<TrackSnapshot, Guid>> Connect();
    TrackSnapshot? Get(Guid id);
    IReadOnlyList<TrackSnapshot> GetAll();
}
