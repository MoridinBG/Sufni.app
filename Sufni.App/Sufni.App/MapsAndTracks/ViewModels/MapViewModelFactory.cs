using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Services;
namespace Sufni.App.MapsAndTracks.ViewModels;

/// <summary>
/// Creates configured <see cref="MapViewModel"/> instances so editor view
/// models do not carry map infrastructure (tile layers) as constructor
/// dependencies. Ownership is unchanged: the consuming view model disposes
/// the created map view model.
/// </summary>
public interface IMapViewModelFactory
{
    MapViewModel Create();
}

internal sealed class MapViewModelFactory(
    ITileLayerService tileLayerService,
    IDialogService dialogService,
    IUiThreadDispatcher uiThreadDispatcher) : IMapViewModelFactory
{
    public MapViewModel Create() => new(tileLayerService, dialogService, uiThreadDispatcher);
}
