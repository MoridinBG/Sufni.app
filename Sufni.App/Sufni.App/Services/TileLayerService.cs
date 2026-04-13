using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Models;

namespace Sufni.App.Services;

public class TileLayerService : ITileLayerService
{
    private static readonly ILogger logger = Log.ForContext<TileLayerService>();
    private readonly IDatabaseService databaseService;
    private const string SelectedLayerIdKey = "Map.SelectedLayerId";
    private const string CustomLayersKey = "Map.CustomLayers";
    private Task? initializationTask;

    public ObservableCollection<TileLayerConfig> AvailableLayers { get; } = new();

    private TileLayerConfig selectedLayer = null!;
    public TileLayerConfig SelectedLayer
    {
        get => selectedLayer;
        set
        {
            if (selectedLayer == value) return;
            selectedLayer = value;
            _ = databaseService.PutAppSettingAsync(SelectedLayerIdKey, value.Id.ToString());
        }
    }

    public TileLayerService(IDatabaseService databaseService)
    {
        this.databaseService = databaseService;
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
            Id = Guid.NewGuid(),
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
            Id = Guid.NewGuid(),
            Name = "OpenCycleMap",
            UrlTemplate = "https://tile.thunderforest.com/cycle/{z}/{x}/{y}.png?apikey=e9ff876252864e9dacc8dbdd83c786e6", // Maybe add a picker for your own key?
            AttributionText = "OpenCycleMap via Thunderforest",
            AttributionUrl = "https://www.thunderforest.com/opencyclemap/",
            MaxZoom = 22,
            IsCustom = false
        };
        AvailableLayers.Add(openCycleMap);

        // Load custom layers
        var customLayersJson = await databaseService.GetAppSettingAsync(CustomLayersKey);
        if (!string.IsNullOrEmpty(customLayersJson))
        {
            try
            {
                var customLayers = JsonSerializer.Deserialize<List<TileLayerConfig>>(customLayersJson);
                if (customLayers != null)
                {
                    foreach (var layer in customLayers)
                    {
                        layer.IsCustom = true;
                        AvailableLayers.Add(layer);
                    }
                }
            }
            catch (Exception ex) { logger.Warning(ex, "Failed to deserialize custom tile layers"); }
        }

        // Load selection
        var selectedIdStr = await databaseService.GetAppSettingAsync(SelectedLayerIdKey);
        if (Guid.TryParse(selectedIdStr, out var selectedId))
        {
            selectedLayer = AvailableLayers.FirstOrDefault(l => l.Id == selectedId) ?? AvailableLayers.First();
        }
        else
        {
            selectedLayer = AvailableLayers.First();
        }
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
            SelectedLayer = AvailableLayers.First();
        }
        await SaveCustomLayersAsync();
    }

    private async Task SaveCustomLayersAsync()
    {
        var customLayers = AvailableLayers.Where(l => l.IsCustom).ToList();
        var json = JsonSerializer.Serialize(customLayers);
        await databaseService.PutAppSettingAsync(CustomLayersKey, json);
    }
}
