using Microsoft.Extensions.Time.Testing;
using NSubstitute;

using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Infrastructure;
namespace Sufni.App.Tests.TestSupport.Doubles;

/// <summary>
/// Map-factory fixture: creates real <see cref="MapViewModel"/> instances
/// over the given tile-layer substitute, so editor tests keep asserting
/// tile-service interaction through the consuming view model.
/// </summary>
public sealed class TestMapViewModelFactory(ITileLayerService tileLayerService) : IMapViewModelFactory
{
    public MapViewModel Create() => new(
        tileLayerService,
        Substitute.For<IMapLayerCoordinator>(),
        Substitute.For<IDialogService>(),
        new MapGeometryFactory(new InlineBackgroundTaskRunner()),
        new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider());
}
