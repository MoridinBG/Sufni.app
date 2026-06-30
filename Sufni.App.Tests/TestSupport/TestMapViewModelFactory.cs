using NSubstitute;
using Sufni.App.Services;
using Sufni.App.ViewModels;

namespace Sufni.App.Tests.Infrastructure;

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
