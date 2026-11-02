using System;
using System.Collections.Generic;
using Sufni.App.MapsAndTracks.Models;

namespace Sufni.App.MapsAndTracks.Services;

public interface ITileLayerService
{
    IObservable<TileLayerState> Observe();
}

public sealed record TileLayerState(IReadOnlyList<TileLayerConfig> AvailableLayers, TileLayerConfig SelectedLayer);
