using System.Reactive.Linq;
using NSubstitute;

using Sufni.App.MapsAndTracks.Services;
namespace Sufni.App.Tests.TestSupport.Extensions;

public static class TileLayerServiceTestExtensions
{
    public static ITileLayerService WithDefaultLayerState(this ITileLayerService service)
    {
        service.Observe().Returns(Observable.Empty<TileLayerState>());
        return service;
    }
}
