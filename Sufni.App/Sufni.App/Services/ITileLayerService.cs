using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Services;

public interface ITileLayerService
{
    ObservableCollection<TileLayerConfig> AvailableLayers { get; }
    TileLayerConfig SelectedLayer { get; set; }
    Task InitializeAsync();
    Task AddCustomLayerAsync(TileLayerConfig config);
    Task RemoveCustomLayerAsync(TileLayerConfig config);
}
