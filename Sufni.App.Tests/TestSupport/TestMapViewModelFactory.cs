using NSubstitute;

using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Infrastructure;
namespace Sufni.App.Tests.TestSupport;

/// <summary>
/// Map-factory fixture: creates real <see cref="MapViewModel"/> instances
/// over the given tile-layer substitute, so editor tests keep asserting
/// tile-service interaction through the consuming view model.
/// </summary>
public sealed class TestMapViewModelFactory(ITileLayerService tileLayerService) : IMapViewModelFactory
{
    public MapViewModel Create() => new(
        tileLayerService,
        Substitute.For<IDialogService>(),
        new InlineUiThreadDispatcher());
}
