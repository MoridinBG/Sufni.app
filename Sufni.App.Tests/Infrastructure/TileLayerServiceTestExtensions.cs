using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Tests.Infrastructure;

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
