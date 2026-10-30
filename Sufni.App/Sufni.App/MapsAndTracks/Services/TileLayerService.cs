using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;

namespace Sufni.App.MapsAndTracks.Services;

/// <summary>Derives the available map layers and selection from persisted preferences.</summary>
public sealed class TileLayerService(IAppPreferences preferences, IUiThreadDispatcher dispatcher)
    : ITileLayerService, IDisposable
{
    private static readonly Guid JawgDarkLayerId = new("0a3b9c1e-4d2f-4a91-bc7e-1b6e4a2c5f01");
    private static readonly Guid OpenCycleMapLayerId = new("7add6eb5-ee7c-4df5-af18-18ac1bfafc7f");
    private readonly ReplaySubject<TileLayerState> state = new(1);
    private readonly TileLayerConfig[] builtInLayers = CreateBuiltInLayers();
    private IDisposable? subscription;
    private Task? initialization;

    public IObservable<TileLayerState> Observe() => state.AsObservable();

    public Task InitializeAsync() => initialization ??= InitializeCoreAsync();

    private Task InitializeCoreAsync()
    {
        subscription = preferences.Map.ObserveChanges()
            .Select(change => Derive(change.Value))
            .ObserveOnUiThread(dispatcher)
            .Subscribe(state);
        return state.FirstAsync().ToTask();
    }

    private TileLayerState Derive(MapPreferencesValue preferences)
    {
        var layers = builtInLayers.Concat(preferences.CustomLayers.Select(layer => layer.AsCustom())).ToArray();
        var selected = layers.FirstOrDefault(layer => layer.Id == preferences.SelectedLayerId) ?? layers[0];
        return new TileLayerState(Array.AsReadOnly(layers), selected);
    }

    public void Dispose()
    {
        subscription?.Dispose();
        state.Dispose();
    }

    private static TileLayerConfig[] CreateBuiltInLayers()
    {
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

        var openCycleMap = new TileLayerConfig
        {
            Id = OpenCycleMapLayerId,
            Name = "OpenCycleMap",
            UrlTemplate = "https://tile.thunderforest.com/cycle/{z}/{x}/{y}.png?apikey=e9ff876252864e9dacc8dbdd83c786e6",
            AttributionText = "OpenCycleMap via Thunderforest",
            AttributionUrl = "https://www.thunderforest.com/opencyclemap/",
            MaxZoom = 22,
            IsCustom = false
        };

        return [jawgDark, openCycleMap];
    }
}
