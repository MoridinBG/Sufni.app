using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.MapsAndTracks.Models;

namespace Sufni.App.MapsAndTracks.Services;

public interface ITileLayerService
{
    IObservable<TileLayerState> Observe();
    Task InitializeAsync();
}

public sealed record TileLayerState(IReadOnlyList<TileLayerConfig> AvailableLayers, TileLayerConfig SelectedLayer);
