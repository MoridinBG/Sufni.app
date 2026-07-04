using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
namespace Sufni.App.MapsAndTracks.Services;

public class TileLayerService : ITileLayerService, IDisposable
{
    // Stable so a selection made on one device resolves to the same layer
    // on another after sync.
    private static readonly Guid JawgDarkLayerId = new("0a3b9c1e-4d2f-4a91-bc7e-1b6e4a2c5f01");
    private static readonly Guid OpenCycleMapLayerId = new("7add6eb5-ee7c-4df5-af18-18ac1bfafc7f");

    private static readonly ILogger logger = Log.ForContext<TileLayerService>();

    private readonly IMapPreferences mapPreferences;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly IDisposable mapPreferenceSubscription;
    // Serializes refresh-after-preference-change bodies. AppPreferences gates
    // applies so sync preference events fire back-to-back.
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private Task? initializationTask;

    public ObservableCollection<TileLayerConfig> AvailableLayers { get; } = new();

    // Seeded with null; consumers see a value only after InitializeAsync.
    private readonly BehaviorSubject<TileLayerConfig?> selectedLayerSubject = new(null);
    public IObservable<TileLayerConfig> SelectedLayerChanges =>
        selectedLayerSubject.Where(l => l is not null).Select(l => l!);

    private TileLayerConfig selectedLayer = null!;
    public TileLayerConfig SelectedLayer => selectedLayer;

    public async Task SetSelectedLayerAsync(TileLayerConfig config)
    {
        if (!TrySetSelectedLayer(config)) return;
        await mapPreferences.SetSelectedLayerIdAsync(config.Id);
    }

    public TileLayerService(IAppPreferences appPreferences, IUiThreadDispatcher? uiThreadDispatcher = null)
    {
        mapPreferences = appPreferences.Map;
        this.uiThreadDispatcher = uiThreadDispatcher ?? new AvaloniaUiThreadDispatcher();

        // Preference changes fire on whichever thread happens to be awaiting
        // ApplySyncDataAsync. Marshal sync refreshes onto the UI thread because
        // AvailableLayers is bound to the UI. Local writes are already reflected
        // by this service's mutating methods.
        mapPreferenceSubscription = mapPreferences.ObserveChanges()
            .Skip(1)
            .Where(change => change.Origin == PreferenceChangeOrigin.SyncApply)
            .Subscribe(change => OnMapPreferencesChanged(change.Value));
    }

    public Task InitializeAsync()
    {
        return initializationTask ??= InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        AvailableLayers.Clear();

        // Add default layers
        var jawgDark = new TileLayerConfig
        {
            Id = JawgDarkLayerId,
            Name = "Jawg Dark",
            UrlTemplate = "https://tile.jawg.io/aa40616c-c117-442e-ae6f-901ffa0e14a4/{z}/{x}/{y}.png?access-token=lK4rYCmlPZb5Fj4GjObrgGYo0IQnEz00hWXR7lpmRUHQ2a9R6jwr8aEpaSJxh5tn",
            AttributionText = "Tiles Courtesy of Jawg Maps",
            AttributionUrl = "https://jawg.io",
            MaxZoom = 22,
            IsCustom = false
        };
        AvailableLayers.Add(jawgDark);

        var openCycleMap = new TileLayerConfig
        {
            Id = OpenCycleMapLayerId,
            Name = "OpenCycleMap",
            UrlTemplate = "https://tile.thunderforest.com/cycle/{z}/{x}/{y}.png?apikey=e9ff876252864e9dacc8dbdd83c786e6", // Maybe add a picker for your own key?
            AttributionText = "OpenCycleMap via Thunderforest",
            AttributionUrl = "https://www.thunderforest.com/opencyclemap/",
            MaxZoom = 22,
            IsCustom = false
        };
        AvailableLayers.Add(openCycleMap);

        var customLayers = await mapPreferences.GetCustomLayersAsync();
        foreach (var layer in customLayers)
        {
            layer.IsCustom = true;
            AvailableLayers.Add(layer);
        }

        var selectedId = await mapPreferences.GetSelectedLayerIdAsync();
        if (selectedId is { } id)
        {
            selectedLayer = AvailableLayers.FirstOrDefault(l => l.Id == id) ?? AvailableLayers.First();
        }
        else
        {
            selectedLayer = AvailableLayers.First();
        }

        selectedLayerSubject.OnNext(selectedLayer);
    }

    public async Task AddCustomLayerAsync(TileLayerConfig config)
    {
        config.IsCustom = true;
        AvailableLayers.Add(config);
        await SaveCustomLayersAsync();
    }

    public async Task RemoveCustomLayerAsync(TileLayerConfig config)
    {
        if (!config.IsCustom) return;
        AvailableLayers.Remove(config);
        if (SelectedLayer == config)
        {
            await SetSelectedLayerAsync(AvailableLayers.First());
        }
        await SaveCustomLayersAsync();
    }

    private async Task SaveCustomLayersAsync()
    {
        var customLayers = AvailableLayers.Where(l => l.IsCustom).ToList();
        await mapPreferences.SetCustomLayersAsync(customLayers);
    }

    public void Dispose()
    {
        mapPreferenceSubscription.Dispose();
        selectedLayerSubject.Dispose();
        refreshGate.Dispose();
    }

    // Field update + subject push without persisting. Selection changes call
    // this then persist; refresh calls this without persisting (the prefs file
    // is the source we just read from).
    private bool TrySetSelectedLayer(TileLayerConfig value)
    {
        if (selectedLayer == value) return false;
        selectedLayer = value;
        selectedLayerSubject.OnNext(value);
        return true;
    }

    private void OnMapPreferencesChanged(MapPreferencesValue preferences)
    {
        if (uiThreadDispatcher.CheckAccess())
        {
            _ = RefreshFromPreferencesSafelyAsync(preferences);
            return;
        }

        _ = uiThreadDispatcher.InvokeAsync(() => RefreshFromPreferencesSafelyAsync(preferences));
    }

    private async Task RefreshFromPreferencesSafelyAsync(MapPreferencesValue preferences)
    {
        await refreshGate.WaitAsync();
        try
        {
            await RefreshFromPreferencesAsync(preferences);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to refresh tile layers from preferences");
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task RefreshFromPreferencesAsync(MapPreferencesValue preferences)
    {
        if (initializationTask is null) return;

        // Serialize against InitializeCoreAsync — both mutate AvailableLayers
        // and selectedLayer.
        await initializationTask;

        for (var i = AvailableLayers.Count - 1; i >= 0; i--)
        {
            if (AvailableLayers[i].IsCustom)
            {
                AvailableLayers.RemoveAt(i);
            }
        }

        foreach (var layer in preferences.CustomLayers)
        {
            layer.IsCustom = true;
            AvailableLayers.Add(layer);
        }

        var resolved = preferences.SelectedLayerId is { } id
            ? AvailableLayers.FirstOrDefault(l => l.Id == id) ?? AvailableLayers.First()
            : AvailableLayers.First();

        TrySetSelectedLayer(resolved);
    }
}
