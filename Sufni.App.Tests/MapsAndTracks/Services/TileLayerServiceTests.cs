using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
namespace Sufni.App.Tests.MapsAndTracks.Services;

[Collection("Ui")]
public class TileLayerServiceTests
{
    [Fact]
    public async Task SelectedLayerChanges_ReplaysCurrentSelection_AfterInitialize()
    {
        using var tempDirectory = new TempDirectory("sufni-tilelayer-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var preferences = new AppPreferences(preferencesPath);
        using var service = new TileLayerService(preferences);
        await service.InitializeAsync();

        TileLayerConfig? observed = null;
        using var subscription = service.SelectedLayerChanges.Subscribe(l => observed = l);

        Assert.NotNull(observed);
        Assert.Same(service.SelectedLayer, observed);
    }

    [Fact]
    public void SelectedLayerChanges_DoesNotEmit_BeforeInitialize()
    {
        using var tempDirectory = new TempDirectory("sufni-tilelayer-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var preferences = new AppPreferences(preferencesPath);
        using var service = new TileLayerService(preferences);

        var emissions = 0;
        using var subscription = service.SelectedLayerChanges.Subscribe(_ => emissions++);

        Assert.Equal(0, emissions);
    }

    [Fact]
    public async Task BuiltInLayerIds_AreStable_AcrossServiceInstances()
    {
        using var tempDirectoryA = new TempDirectory("sufni-tilelayer-test");
        var preferencesPathA = Path.Combine(tempDirectoryA.Path, "app-preferences.json");
        using var tempDirectoryB = new TempDirectory("sufni-tilelayer-test");
        var preferencesPathB = Path.Combine(tempDirectoryB.Path, "app-preferences.json");
        using var serviceA = new TileLayerService(new AppPreferences(preferencesPathA));
        using var serviceB = new TileLayerService(new AppPreferences(preferencesPathB));
        await serviceA.InitializeAsync();
        await serviceB.InitializeAsync();

        var aIds = serviceA.AvailableLayers.Where(l => !l.IsCustom).Select(l => l.Id).ToList();
        var bIds = serviceB.AvailableLayers.Where(l => !l.IsCustom).Select(l => l.Id).ToList();

        Assert.Equal(aIds, bIds);
        Assert.All(aIds, id => Assert.NotEqual(Guid.Empty, id));
    }

    [Fact]
    public async Task SelectedLayerChanges_PushesEachDistinctSetterAssignment()
    {
        using var tempDirectory = new TempDirectory("sufni-tilelayer-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var preferences = new AppPreferences(preferencesPath);
        using var service = new TileLayerService(preferences);
        await service.InitializeAsync();

        var observed = new List<TileLayerConfig>();
        using var subscription = service.SelectedLayerChanges.Subscribe(observed.Add);

        var initial = service.SelectedLayer;
        var other = service.AvailableLayers.First(l => l != initial);

        await service.SetSelectedLayerAsync(other);
        await service.SetSelectedLayerAsync(other); // no-op: equality short-circuit
        await service.SetSelectedLayerAsync(initial);

        Assert.Equal([initial, other, initial], observed);
    }

    [AvaloniaFact]
    public async Task ApplySyncDataAsync_RefreshesAvailableLayersAndSelectedLayer()
    {
        using var tempDirectory = new TempDirectory("sufni-tilelayer-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var preferences = new AppPreferences(preferencesPath);
        using var service = new TileLayerService(preferences);
        await service.InitializeAsync();

        var initial = service.SelectedLayer;

        var syncedLayer = new TileLayerConfig
        {
            Id = Guid.NewGuid(),
            Name = "Synced custom",
            UrlTemplate = "https://tiles.example/{z}/{x}/{y}.png",
            AttributionText = "Example",
            AttributionUrl = "https://tiles.example",
            MaxZoom = 18,
            IsCustom = true,
        };

        // Skip the BehaviorSubject replay of the initial value; the next
        // emission is the one driven by the refresh.
        var nextSelection = service.SelectedLayerChanges.Skip(1).FirstAsync().ToTask();

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 100,
            Maps = new MapPreferencesSyncData
            {
                SelectedLayerId = syncedLayer.Id,
                CustomLayers = [syncedLayer],
            },
        });

        var observed = await nextSelection.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(syncedLayer.Id, observed.Id);
        Assert.NotSame(initial, service.SelectedLayer);
        Assert.Equal(syncedLayer.Id, service.SelectedLayer.Id);
        Assert.Contains(service.AvailableLayers, l => l.Id == syncedLayer.Id);
    }

    [AvaloniaFact]
    public async Task ApplySyncDataAsync_BeforeInitialize_DoesNotRefresh()
    {
        using var tempDirectory = new TempDirectory("sufni-tilelayer-test");
        var preferencesPath = Path.Combine(tempDirectory.Path, "app-preferences.json");
        var preferences = new AppPreferences(preferencesPath);
        using var service = new TileLayerService(preferences);

        await preferences.ApplySyncDataAsync(new AppPreferencesSyncData
        {
            Updated = 100,
            Maps = new MapPreferencesSyncData
            {
                SelectedLayerId = Guid.NewGuid(),
                CustomLayers = [],
            },
        });

        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Refresh skipped: no init has happened, so AvailableLayers
        // remains empty and InitializeAsync still reads fresh state.
        Assert.Empty(service.AvailableLayers);

        await service.InitializeAsync();
        Assert.NotEmpty(service.AvailableLayers);
    }

}
