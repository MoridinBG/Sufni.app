using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.TestSupport.Fixtures;

internal sealed class MapLayerFixture : IDisposable
{
    public InMemoryMapPreferences Map { get; } = new();
    public InMemorySynchronizableRepository<CustomTileLayer> Repository { get; } = new(PersistenceTestData.CreateTimeProvider());
    public CustomTileLayerStore Store { get; }
    public StorePublication Publication { get; }
    private readonly IAppPreferences preferences = Substitute.For<IAppPreferences>();
    private readonly IUiThreadDispatcher dispatcher;

    public MapLayerFixture(IUiThreadDispatcher? dispatcher = null)
    {
        this.dispatcher = dispatcher ?? new InlineUiThreadDispatcher();
        Publication = new(this.dispatcher);
        Store = new(Publication);
        preferences.Map.Returns(Map);
    }

    public MapLayerCoordinator CreateCoordinator() => new(preferences, new InlineBackgroundTaskRunner(), Repository, Store);
    public TileLayerService CreateService() => new(preferences, dispatcher, Store);
    public async Task SeedAsync(TileLayerConfig config)
    {
        var row = CustomTileLayer.From(config);
        Repository.Seed(row);
        await Store.PublishChangedAsync([CustomTileLayerSnapshot.From(row)]);
    }

    public void Dispose()
    {
        Store.Dispose();
        Publication.Dispose();
        Map.Dispose();
    }
}
