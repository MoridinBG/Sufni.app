using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

using Sufni.App.MapsAndTracks.Models;
namespace Sufni.App.MapsAndTracks.Services;

public interface ITileLayerService
{
    ObservableCollection<TileLayerConfig> AvailableLayers { get; }
    TileLayerConfig SelectedLayer { get; }

    // Emits the current selection then every subsequent change.
    IObservable<TileLayerConfig> SelectedLayerChanges { get; }

    Task InitializeAsync();
    Task SetSelectedLayerAsync(TileLayerConfig config);
    Task AddCustomLayerAsync(TileLayerConfig config);
    Task RemoveCustomLayerAsync(TileLayerConfig config);
}
