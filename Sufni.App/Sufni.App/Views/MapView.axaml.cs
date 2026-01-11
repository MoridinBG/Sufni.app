using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using BruTile;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Nts.Extensions;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Views;

public class MapView : TemplatedControl
{
    private MapControl? mapControl;
    private ITileLayerService? tileLayerService;
    private IDialogService? dialogService;
    private ComboBox? tileProviderComboBox;
    private Button? addCustomTileButton;

    private readonly WritableLayer positionMarkerLayer = new()
    {
        Name = "Position Marker",
        Style = new SymbolStyle { SymbolScale = 0.5 }
    };

    public static readonly StyledProperty<List<TrackPoint>?> TrackPointsProperty =
        AvaloniaProperty.Register<MapView, List<TrackPoint>?>(nameof(TrackPoints));
    
    public List<TrackPoint>? TrackPoints
    {
        get => GetValue(TrackPointsProperty);
        set => SetValue(TrackPointsProperty, value);
    }

    public static readonly StyledProperty<List<TrackPoint>?> SessionTrackPointsProperty =
        AvaloniaProperty.Register<MapView, List<TrackPoint>?>(nameof(SessionTrackPoints));
    
    public List<TrackPoint>? SessionTrackPoints
    {
        get => GetValue(SessionTrackPointsProperty);
        set => SetValue(SessionTrackPointsProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (App.Current?.Services != null)
        {
            tileLayerService = App.Current.Services.GetService<ITileLayerService>();
            dialogService = App.Current.Services.GetService<IDialogService>();
            _ = tileLayerService?.InitializeAsync().ContinueWith(t => 
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateSelectedLayer));
        }

        mapControl = e.NameScope.Find<MapControl>("MapControl");
        tileProviderComboBox = e.NameScope.Find<ComboBox>("TileProviderComboBox");
        addCustomTileButton = e.NameScope.Find<Button>("AddCustomTileButton");

        if (tileProviderComboBox != null && tileLayerService != null)
        {
            tileProviderComboBox.ItemsSource = tileLayerService.AvailableLayers;
            tileProviderComboBox.SelectionChanged += TileProviderComboBox_SelectionChanged;
        }

        if (addCustomTileButton != null)
        {
            addCustomTileButton.Click += AddCustomTileButton_Click;
        }

        var trackLayer = CreateFullTrackLayer();
        mapControl?.Map.Layers.Add(trackLayer);

        var sessionTrackLayer = CreateSessionTrackLayer();
        mapControl?.Map.Layers.Add(sessionTrackLayer);
        mapControl?.Map.Layers.Add(CreateStartEndPointsLayer());
        mapControl?.Map.Layers.Add(positionMarkerLayer);
        mapControl?.Map.Navigator.CenterOnAndZoomTo(sessionTrackLayer.Extent!.Centroid, 10);

        SetNormalizedCursorPosition(100);
    }

    private async void AddCustomTileButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (dialogService == null || tileLayerService == null) return;
        
        var config = await dialogService.ShowAddTileLayerDialogAsync();
        if (config != null)
        {
            await tileLayerService.AddCustomLayerAsync(config);
            tileLayerService.SelectedLayer = config;
            UpdateSelectedLayer();
        }
    }

    private void TileProviderComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (tileLayerService == null || tileProviderComboBox?.SelectedItem is not TileLayerConfig selected) return;
        
        tileLayerService.SelectedLayer = selected;
        UpdateTileLayer(selected);
    }

    private void UpdateSelectedLayer()
    {
        if (tileLayerService == null || tileProviderComboBox == null) return;
        tileProviderComboBox.SelectedItem = tileLayerService.SelectedLayer;
        UpdateTileLayer(tileLayerService.SelectedLayer);
    }

    private void UpdateTileLayer(TileLayerConfig config)
    {
        if (mapControl == null) return;

        // Remove existing tile layers
        var tileLayers = mapControl.Map.Layers.OfType<TileLayer>().ToList();
        foreach (var layer in tileLayers)
        {
            mapControl.Map.Layers.Remove(layer);
        }

        // Create and add new layer
        var layerToAdd = CreateTileLayer(config);
        
        // Insert at index 0 to be behind everything else
        mapControl.Map.Layers.Insert(0, layerToAdd);
        mapControl.Refresh();
    }

    private static TileLayer CreateTileLayer(TileLayerConfig config)
    {
        var tileSource = new HttpTileSource(
            new GlobalSphericalMercator(0, config.MaxZoom, null),
            config.UrlTemplate,
            name: config.Name,
            attribution: new Attribution(config.AttributionText, config.AttributionUrl)
        );

        return new TileLayer(tileSource) { Name = config.Name };
    }

    private MemoryLayer CreateFullTrackLayer()
    {
        Debug.Assert(TrackPoints is not null);

        var lineString = new LineString(TrackPoints.Select(v => (v.X, v.Y).ToCoordinate()).ToArray());
        var style = new VectorStyle { Line = new Pen(Color.FromString("#abdda4"), 2) }; // Spectral11 #04

        return new MemoryLayer
        {
            Features = [new GeometryFeature { Geometry = lineString }],
            Name = "Full Track",
            Style = style
        };
    }

    private MemoryLayer CreateStartEndPointsLayer()
    {
        Debug.Assert(SessionTrackPoints is not null);

        var startPointFeature = new PointFeature(SessionTrackPoints[0].X, SessionTrackPoints[0].Y);
        startPointFeature.Styles.Add(new SymbolStyle 
        {
            SymbolType = SymbolType.Ellipse,
            Line = new Pen(Color.Black),
            Fill = new Brush(Color.FromString("#229954")),
            SymbolScale = 0.5
        });

        var endPointFeature = new PointFeature(SessionTrackPoints[^1].X, SessionTrackPoints[^1].Y);
        endPointFeature.Styles.Add(new SymbolStyle 
        {
            SymbolType = SymbolType.Ellipse,
            Line = new Pen(Color.Black),
            Fill = new Brush(Color.FromString("#E74C3C")),
            SymbolScale = 0.5
        });

        return new MemoryLayer
        {
            Features = [startPointFeature, endPointFeature],
            Style = new SymbolStyle { SymbolScale = 0.5 },
            Name = "Start/End Marker"
        };
    }

    public void SetNormalizedCursorPosition(double pos)
    {
        if (SessionTrackPoints is null || pos < 0 || pos > 1) return;

        var index = (int)Math.Ceiling((SessionTrackPoints.Count - 1) * pos);
        positionMarkerLayer.Clear();
        var feature = new PointFeature(SessionTrackPoints[index].X, SessionTrackPoints[index].Y);
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Line = new Pen(Color.Black),
            Fill = new Brush(Color.Gray),
            SymbolScale = 0.5
        });
        positionMarkerLayer.Add(feature);
        positionMarkerLayer.DataHasChanged();
    }

    public void ZoomToNormalizedRange(double startNormalized, double endNormalized, double padding = 0.1)
    {
        if (SessionTrackPoints is null || mapControl is null || startNormalized >= endNormalized) return;

        // Clamp to valid range
        startNormalized = Math.Clamp(startNormalized, 0, 1);
        endNormalized = Math.Clamp(endNormalized, 0, 1);
        
        var startIndex = (int)Math.Floor((SessionTrackPoints.Count - 1) * startNormalized);
        var endIndex = (int)Math.Ceiling((SessionTrackPoints.Count - 1) * endNormalized);
        
        startIndex = Math.Max(0, startIndex);
        endIndex = Math.Min(SessionTrackPoints.Count - 1, endIndex);
        if (startIndex >= endIndex) return;
        
        var pointsInRange = SessionTrackPoints.Skip(startIndex).Take(endIndex - startIndex + 1).ToList();
        var minX = pointsInRange.Min(p => p.X);
        var maxX = pointsInRange.Max(p => p.X);
        var minY = pointsInRange.Min(p => p.Y);
        var maxY = pointsInRange.Max(p => p.Y);
        
        var width = maxX - minX;
        var height = maxY - minY;
        var paddingX = width * padding;
        var paddingY = height * padding;

        var extent = new Mapsui.MRect(
            minX - paddingX,
            minY - paddingY,
            maxX + paddingX,
            maxY + paddingY);
        Console.WriteLine(extent);

        mapControl.Map.Navigator.ZoomToBox(extent);
    }

    private MemoryLayer CreateSessionTrackLayer()
    {
        Debug.Assert(SessionTrackPoints is not null);

        var lineString = new LineString(SessionTrackPoints.Select(v => (v.X, v.Y).ToCoordinate()).ToArray());
        var style = new VectorStyle { Line = new Pen(Color.FromString("#9e0142"), 5) }; // Spectral11 #11

        var sessionTrackFeature = new GeometryFeature(lineString);
        sessionTrackFeature.Styles.Add(style);

        return new MemoryLayer
        {
            Features = [new GeometryFeature { Geometry = lineString }],
            Name = "Session Track",
            Style = style
        };
    }
}