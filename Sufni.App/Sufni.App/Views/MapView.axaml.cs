using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Nts.Extensions;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Avalonia;
using NetTopologySuite.Geometries;
using Sufni.App.Models;
using Sufni.App.ViewModels;

namespace Sufni.App.Views;

public partial class MapView : UserControl
{
    private MapControl? mapControl;

    private readonly WritableLayer positionMarkerLayer = new()
    {
        Name = "Position Marker",
        Style = new SymbolStyle { SymbolScale = 0.5 }
    };

    public event Action<double, double>? ViewportNormalizedRangeChanged;

    public MapViewModel? ViewModel => DataContext as MapViewModel;

    public MapView()
    {
        InitializeComponent();

        mapControl = this.FindControl<MapControl>("MapControl");

        // Setup initial layers
        if (mapControl != null)
        {
            var trackLayer = CreateFullTrackLayer(); // Initially empty until ViewModel updates
            mapControl.Map.Layers.Add(trackLayer);

            var sessionTrackLayer = CreateSessionTrackLayer(); // Initially empty
            mapControl.Map.Layers.Add(sessionTrackLayer);
            mapControl.Map.Layers.Add(CreateStartEndPointsLayer());
            mapControl.Map.Layers.Add(positionMarkerLayer);

            mapControl.PointerReleased += (_, _) => NotifyViewportChanged();
            mapControl.PointerWheelChanged += (_, _) => NotifyViewportChanged();
        }

        SetNormalizedCursorPosition(1);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel != null)
        {
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            // Initial sync
            if (ViewModel.SelectedLayer != null)
                UpdateTileLayer(ViewModel.SelectedLayer);
            
            UpdateTracks();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel == null) return;

        if (e.PropertyName == nameof(MapViewModel.SelectedLayer) && ViewModel.SelectedLayer != null)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => UpdateTileLayer(ViewModel.SelectedLayer));
        }
        else if (e.PropertyName == nameof(MapViewModel.FullTrackPoints) || e.PropertyName == nameof(MapViewModel.SessionTrackPoints))
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateTracks);
        }
    }

    private void UpdateTracks()
    {
        if (mapControl == null || ViewModel == null) return;

        // Update Full Track
        var fullTrackLayer = mapControl.Map.Layers.FindLayer("Full Track").FirstOrDefault() as MemoryLayer;
        if (fullTrackLayer != null && ViewModel.FullTrackPoints != null)
        {
            var lineString = new LineString(ViewModel.FullTrackPoints.Select(v => (v.X, v.Y).ToCoordinate()).ToArray());
            fullTrackLayer.Features = [new GeometryFeature { Geometry = lineString }];
            fullTrackLayer.DataHasChanged();
        }

        // Update Session Track
        var sessionTrackLayer = mapControl.Map.Layers.FindLayer("Session Track").FirstOrDefault() as MemoryLayer;
        if (sessionTrackLayer != null && ViewModel.SessionTrackPoints != null)
        {
            var lineString = new LineString(ViewModel.SessionTrackPoints.Select(v => (v.X, v.Y).ToCoordinate()).ToArray());
            sessionTrackLayer.Features = [new GeometryFeature { Geometry = lineString }];
            sessionTrackLayer.DataHasChanged();

            // Zoom to session
            if (sessionTrackLayer.Extent != null)
            {
                mapControl.Map.Navigator.CenterOnAndZoomTo(sessionTrackLayer.Extent.Centroid, 10);
            }
        }

        // Update Markers
        var markerLayer = mapControl.Map.Layers.FindLayer("Start/End Marker").FirstOrDefault() as MemoryLayer;
        if (markerLayer != null && ViewModel.SessionTrackPoints != null && ViewModel.SessionTrackPoints.Any())
        {
            var start = ViewModel.SessionTrackPoints.First();
            var end = ViewModel.SessionTrackPoints.Last();
            
            var startPointFeature = new PointFeature(start.X, start.Y);
            startPointFeature.Styles.Add(new SymbolStyle { SymbolType = SymbolType.Ellipse, Line = new Pen(Color.Black), Fill = new Brush(Color.FromString("#229954")), SymbolScale = 0.5 });
            
            var endPointFeature = new PointFeature(end.X, end.Y);
            endPointFeature.Styles.Add(new SymbolStyle { SymbolType = SymbolType.Ellipse, Line = new Pen(Color.Black), Fill = new Brush(Color.FromString("#E74C3C")), SymbolScale = 0.5 });

            markerLayer.Features = [startPointFeature, endPointFeature];
            markerLayer.DataHasChanged();
        }

        mapControl.Refresh();
    }

    private void UpdateTileLayer(TileLayerConfig config)
    {
        if (mapControl == null) return;

        var tileLayers = mapControl.Map.Layers.OfType<TileLayer>().ToList();
        foreach (var layer in tileLayers)
        {
            mapControl.Map.Layers.Remove(layer);
        }

        var layerToAdd = CreateTileLayer(config);
        mapControl.Map.Layers.Insert(0, layerToAdd);
        mapControl.Refresh();
    }

    private static TileLayer CreateTileLayer(TileLayerConfig config)
    {
        var tileSource = new HttpTileSource(
            new GlobalSphericalMercator(minZoomLevel: 0, maxZoomLevel: config.MaxZoom, name: null),
            config.UrlTemplate,
            name: config.Name,
            attribution: new BruTile.Attribution(config.AttributionText, config.AttributionUrl)
        );

        return new TileLayer(tileSource) { Name = config.Name };
    }

    private MemoryLayer CreateFullTrackLayer()
    {
        var style = new VectorStyle { Line = new Pen(Color.FromString("#abdda4"), 2) };
        return new MemoryLayer { Name = "Full Track", Style = style };
    }

    private MemoryLayer CreateStartEndPointsLayer()
    {
        return new MemoryLayer { Name = "Start/End Marker", Style = new SymbolStyle { SymbolScale = 0.5 } };
    }

    public void SetNormalizedCursorPosition(double pos)
    {
        if (ViewModel?.SessionTrackPoints == null || pos < 0 || pos > 1) return;

        var index = (int)Math.Ceiling((ViewModel.SessionTrackPoints.Count - 1) * pos);
        positionMarkerLayer.Clear();
        var feature = new PointFeature(ViewModel.SessionTrackPoints[index].X, ViewModel.SessionTrackPoints[index].Y);
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Line = new Pen(Color.Black),
            Fill = new Brush(Color.Gray),
            SymbolScale = 0.5
        });
        positionMarkerLayer.Add(feature);
        positionMarkerLayer.DataHasChanged();
        mapControl?.Refresh();
    }

    public void ZoomToNormalizedRange(double startNormalized, double endNormalized, double padding = 0.1)
    {
        if (ViewModel?.SessionTrackPoints == null || mapControl == null || startNormalized >= endNormalized) return;

        startNormalized = Math.Clamp(startNormalized, 0, 1);
        endNormalized = Math.Clamp(endNormalized, 0, 1);
        
        var startIndex = (int)Math.Floor((ViewModel.SessionTrackPoints.Count - 1) * startNormalized);
        var endIndex = (int)Math.Ceiling((ViewModel.SessionTrackPoints.Count - 1) * endNormalized);
        
        startIndex = Math.Max(0, startIndex);
        endIndex = Math.Min(ViewModel.SessionTrackPoints.Count - 1, endIndex);
        if (startIndex >= endIndex) return;
        
        var pointsInRange = ViewModel.SessionTrackPoints.Skip(startIndex).Take(endIndex - startIndex + 1).ToList();
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

        mapControl.Map.Navigator.ZoomToBox(extent);
    }

    private void NotifyViewportChanged()
    {
        if (ViewModel?.SessionTrackPoints == null || mapControl == null) return;

        var viewport = mapControl.Map.Navigator.Viewport;
        var halfWidth = viewport.Width * viewport.Resolution / 2;
        var halfHeight = viewport.Height * viewport.Resolution / 2;
        var minX = viewport.CenterX - halfWidth;
        var maxX = viewport.CenterX + halfWidth;
        var minY = viewport.CenterY - halfHeight;
        var maxY = viewport.CenterY + halfHeight;

        int firstVisible = -1, lastVisible = -1;
        for (var i = 0; i < ViewModel.SessionTrackPoints.Count; i++)
        {
            var p = ViewModel.SessionTrackPoints[i];
            if (p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY)
            {
                if (firstVisible == -1) firstVisible = i;
                lastVisible = i;
            }
        }

        if (firstVisible < 0 || lastVisible <= firstVisible) return;

        var count = ViewModel.SessionTrackPoints.Count - 1;
        ViewportNormalizedRangeChanged?.Invoke((double)firstVisible / count, (double)lastVisible / count);
    }

    private MemoryLayer CreateSessionTrackLayer()
    {
        var style = new VectorStyle { Line = new Pen(Color.FromString("#9e0142"), 5) };
        return new MemoryLayer { Name = "Session Track", Style = style };
    }
}
