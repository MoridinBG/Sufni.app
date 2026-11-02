using Sufni.App.Tests.TestSupport.Fixtures;
using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.MapsAndTracks.Services;

public class TileLayerServiceTests
{
    [Fact]
    public async Task Observe_ReplaysCatalogAndSelection_FromCurrentPreferences()
    {
        using var fixture = new MapLayerFixture();
        var map = fixture.Map;
        var custom = new TileLayerConfig { Name = "Custom" };
        await fixture.SeedAsync(custom);
        map.Seed(custom.Id);
        using var service = fixture.CreateService();

        TileLayerState? observed = null;
        using var subscription = service.Observe().Subscribe(state => observed = state);

        Assert.NotNull(observed);
        Assert.Equal(custom.Id, observed.SelectedLayer.Id);
        Assert.Contains(observed.SelectedLayer, observed.AvailableLayers);
        Assert.True(observed.SelectedLayer.IsCustom);
        Assert.Equal(2, observed.AvailableLayers.Count(layer => !layer.IsCustom));
    }

    [Fact]
    public async Task Observe_FollowsStoreArrivalsWithoutChangingSelection_AndFallsBackWhenSelectedLayerDisappears()
    {
        using var fixture = new MapLayerFixture();
        var map = fixture.Map;
        using var service = fixture.CreateService();
        var observed = new List<TileLayerState>();
        using var subscription = service.Observe().Subscribe(observed.Add);
        var builtIn = observed[^1].SelectedLayer;
        var local = new TileLayerConfig { Name = "Local" };

        await fixture.SeedAsync(local);
        await map.SetSelectedLayerIdAsync(local.Id);
        Assert.Equal(local.Id, observed[^1].SelectedLayer.Id);

        var remote = new TileLayerConfig { Name = "Remote" };
        await fixture.SeedAsync(remote);
        Assert.Equal(local.Id, observed[^1].SelectedLayer.Id);
        Assert.Contains(observed[^1].AvailableLayers, layer => layer.Id == remote.Id);

        await fixture.Store.PublishRemovedAsync([local.Id]);
        Assert.Equal(builtIn.Id, observed[^1].SelectedLayer.Id);
        Assert.All(observed, state => Assert.Contains(state.SelectedLayer, state.AvailableLayers));
    }

    [Fact]
    public async Task Observe_PublishesOnDispatcher_AndStopsAfterDisposal()
    {
        var dispatcher = new ManualUiThreadDispatcher();
        using var fixture = new MapLayerFixture(dispatcher);
        var map = fixture.Map;
        using var service = fixture.CreateService();
        var observed = new List<TileLayerState>();
        using var subscription = service.Observe().Subscribe(observed.Add);
        Assert.Empty(observed);

        await dispatcher.DrainAsync();
        Assert.Single(observed);

        map.Seed(Guid.NewGuid());
        service.Dispose();
        await dispatcher.DrainAsync();
        Assert.Single(observed);
    }

    [Fact]
    public void BuiltInLayers_HaveStableIds_AcrossServiceInstances()
    {
        using var firstMap = new MapLayerFixture();
        using var secondMap = new MapLayerFixture();
        using var first = firstMap.CreateService();
        using var second = secondMap.CreateService();
        TileLayerState? firstState = null;
        TileLayerState? secondState = null;
        using var firstSubscription = first.Observe().Subscribe(state => firstState = state);
        using var secondSubscription = second.Observe().Subscribe(state => secondState = state);

        Assert.Equal(firstState!.AvailableLayers.Select(layer => layer.Id),
            secondState!.AvailableLayers.Select(layer => layer.Id));
    }

}
