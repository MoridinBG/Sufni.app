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
        using var map = new InMemoryMapPreferences();
        var custom = new TileLayerConfig { Name = "Custom" };
        map.ApplySynced(new(custom.Id, [custom]));
        using var service = CreateService(map);
        await service.InitializeAsync();

        TileLayerState? observed = null;
        using var subscription = service.Observe().Subscribe(state => observed = state);

        Assert.NotNull(observed);
        Assert.Equal(custom.Id, observed.SelectedLayer.Id);
        Assert.Contains(observed.SelectedLayer, observed.AvailableLayers);
        Assert.True(observed.SelectedLayer.IsCustom);
        Assert.Equal(2, observed.AvailableLayers.Count(layer => !layer.IsCustom));
    }

    [Fact]
    public async Task Observe_FollowsLocalAndSyncedPreferences_AndFallsBackWhenSelectedLayerDisappears()
    {
        using var map = new InMemoryMapPreferences();
        using var service = CreateService(map);
        var observed = new List<TileLayerState>();
        using var subscription = service.Observe().Subscribe(observed.Add);
        await service.InitializeAsync();
        var builtIn = observed[^1].SelectedLayer;
        var local = new TileLayerConfig { Name = "Local" };

        await map.SetCustomLayersAsync([local]);
        await map.SetSelectedLayerIdAsync(local.Id);
        Assert.Equal(local.Id, observed[^1].SelectedLayer.Id);

        var remote = new TileLayerConfig { Name = "Remote" };
        map.ApplySynced(new(remote.Id, [remote]));
        Assert.Equal(remote.Id, observed[^1].SelectedLayer.Id);
        Assert.DoesNotContain(observed[^1].AvailableLayers, layer => layer.Id == local.Id);

        map.ApplySynced(new(remote.Id, []));
        Assert.Equal(builtIn.Id, observed[^1].SelectedLayer.Id);
        Assert.All(observed, state => Assert.Contains(state.SelectedLayer, state.AvailableLayers));
    }

    [Fact]
    public async Task Observe_PublishesOnDispatcher_AndStopsAfterDisposal()
    {
        using var map = new InMemoryMapPreferences();
        var dispatcher = new ManualUiThreadDispatcher();
        using var service = CreateService(map, dispatcher);
        var observed = new List<TileLayerState>();
        using var subscription = service.Observe().Subscribe(observed.Add);
        var initialization = service.InitializeAsync();
        Assert.Empty(observed);
        Assert.False(initialization.IsCompleted);

        await dispatcher.DrainAsync();
        await initialization.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Single(observed);

        map.ApplySynced(new(Guid.NewGuid(), []));
        service.Dispose();
        await dispatcher.DrainAsync();
        Assert.Single(observed);
    }

    [Fact]
    public async Task BuiltInLayers_HaveStableIds_AcrossServiceInstances()
    {
        using var firstMap = new InMemoryMapPreferences();
        using var secondMap = new InMemoryMapPreferences();
        using var first = CreateService(firstMap);
        using var second = CreateService(secondMap);
        TileLayerState? firstState = null;
        TileLayerState? secondState = null;
        using var firstSubscription = first.Observe().Subscribe(state => firstState = state);
        using var secondSubscription = second.Observe().Subscribe(state => secondState = state);
        await first.InitializeAsync();
        await second.InitializeAsync();

        Assert.Equal(firstState!.AvailableLayers.Select(layer => layer.Id),
            secondState!.AvailableLayers.Select(layer => layer.Id));
    }

    private static TileLayerService CreateService(InMemoryMapPreferences map, IUiThreadDispatcher? dispatcher = null)
    {
        var preferences = Substitute.For<IAppPreferences>();
        preferences.Map.Returns(map);
        return new TileLayerService(preferences, dispatcher ?? new InlineUiThreadDispatcher());
    }
}
