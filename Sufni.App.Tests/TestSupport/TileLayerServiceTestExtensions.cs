using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Services;

using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Models;
namespace Sufni.App.Tests.TestSupport;

public static class TileLayerServiceTestExtensions
{
    // NSubstitute returns null for IObservable<T> by default; MapViewModel
    // subscribes during construction and would NRE. This installs an empty
    // observable so the subscribe is a no-op for tests that don't care.
    public static ITileLayerService WithDefaultSelectedLayerChanges(this ITileLayerService service)
    {
        service.SelectedLayerChanges.Returns(Observable.Empty<TileLayerConfig>());
        service.SetSelectedLayerAsync(Arg.Any<TileLayerConfig>()).Returns(Task.CompletedTask);
        return service;
    }
}
