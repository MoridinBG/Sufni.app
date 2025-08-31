using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using BruTile;
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

namespace Sufni.App.Views;

public class MapView : TemplatedControl
{
    public MapControl? MapControl;

    public static readonly StyledProperty<List<TrackPoint>> TrackPointsProperty =
        AvaloniaProperty.Register<MapView, List<TrackPoint>>(nameof(TrackPoints));
    
    public List<TrackPoint> TrackPoints
    {
        get => GetValue(TrackPointsProperty);
        set => SetValue(TrackPointsProperty, value);
    }

    public static readonly StyledProperty<List<TrackPoint>> SessionTrackPointsProperty =
        AvaloniaProperty.Register<MapView, List<TrackPoint>>(nameof(SessionTrackPoints));
    
    public List<TrackPoint> SessionTrackPoints
    {
        get => GetValue(SessionTrackPointsProperty);
        set => SetValue(SessionTrackPointsProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        MapControl = e.NameScope.Find<MapControl>("MapControl");
        MapControl?.Map.Layers.Add(CreateJawgTileLayer());

        var trackLayer = CreateFullTrackLayer();
        MapControl?.Map.Layers.Add(trackLayer);

        var sessionTrackLayer = CreateSessionTrackLayer();
        MapControl?.Map.Layers.Add(sessionTrackLayer);
        MapControl?.Map.Layers.Add(CreateStartEndPointsLayer());
        MapControl?.Map.Navigator.CenterOnAndZoomTo(sessionTrackLayer.Extent!.Centroid, 10);
    }

    private static TileLayer CreateJawgTileLayer()
    {
        var tileSource = new HttpTileSource(
            new GlobalSphericalMercator(),
            "https://tile.jawg.io/aa40616c-c117-442e-ae6f-901ffa0e14a4/{z}/{x}/{y}.png?access-token=lK4rYCmlPZb5Fj4GjObrgGYo0IQnEz00hWXR7lpmRUHQ2a9R6jwr8aEpaSJxh5tn",
            name: "Jawg Dark",
            attribution: new Attribution("Tiles Courtesy of Jawg Maps", "https://jawg.io")
        );

        return new TileLayer(tileSource) { Name = "Jawg Dark" };
    }

    private MemoryLayer CreateFullTrackLayer()
    {
        var lineString = new LineString(TrackPoints.Select(v => (v.X, v.Y).ToCoordinate()).ToArray());
        var style = new VectorStyle { Line = new Pen(Color.FromString("#abdda4"), 2) }; // Spectral11 #04

        return new MemoryLayer
        {
            Features = [new GeometryFeature { Geometry = lineString }],
            Name = "FullTrackLayer",
            Style = style
        };
    }

    private MemoryLayer CreateStartEndPointsLayer()
    {
        var startPointFeature = new PointFeature(SessionTrackPoints[0].X, SessionTrackPoints[0].Y);
        startPointFeature.Styles.Add(new SymbolStyle 
        {
            SymbolType = SymbolType.Ellipse,
            Fill = new Brush(Color.FromString("#229954")),
            SymbolScale = 0.5
        });

        var endPointFeature = new PointFeature(SessionTrackPoints[^1].X, SessionTrackPoints[^1].Y);
        endPointFeature.Styles.Add(new SymbolStyle 
        {
            SymbolType = SymbolType.Ellipse,
            Fill = new Brush(Color.FromString("#E74C3C")),
            SymbolScale = 0.5
        });

        return new MemoryLayer
        {
            Features = [startPointFeature, endPointFeature],
            Style = new SymbolStyle { SymbolScale = 0.5 }
        };
    }

    private MemoryLayer CreateSessionTrackLayer()
    {
        var lineString = new LineString(SessionTrackPoints.Select(v => (v.X, v.Y).ToCoordinate()).ToArray());
        var style = new VectorStyle { Line = new Pen(Color.FromString("#9e0142"), 5) }; // Spectral11 #11

        var sessionTrackFeature = new GeometryFeature(lineString);
        sessionTrackFeature.Styles.Add(style);

        return new MemoryLayer
        {
            Features = [new GeometryFeature { Geometry = lineString }],
            Name = "SessionTrackLayer",
            Style = style
        };
    }
}