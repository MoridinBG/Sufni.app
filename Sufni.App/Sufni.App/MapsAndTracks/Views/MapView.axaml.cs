using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Nts.Extensions;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Avalonia;
using Mapsui.Widgets;
using Mapsui.Widgets.InfoWidgets;
using NetTopologySuite.Geometries;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Models;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Graph.ViewModels.Editors;
namespace Sufni.App.MapsAndTracks.Views;

public partial class MapView : UserControl
{
    private const string ExtensionOverlayLayerName = "Extension Overlays";

    private MapControl? mapControl;
    private RecordedSessionExtensionSlots? subscribedSlots;
    private readonly MapInteractionController interaction = new(
        action => Dispatcher.UIThread.Post(action, DispatcherPriority.Background));

    private readonly WritableLayer positionMarkerLayer = new()
    {
        Name = "Position Marker",
        Style = new SymbolStyle { SymbolScale = 0.5 }
    };

    public static readonly StyledProperty<SessionTimelineLinkViewModel?> TimelineProperty =
        AvaloniaProperty.Register<MapView, SessionTimelineLinkViewModel?>(nameof(Timeline));

    public SessionTimelineLinkViewModel? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly StyledProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.Register<MapView, RecordedSessionExtensionSlots?>(
            nameof(ExtensionSlots));

    public RecordedSessionExtensionSlots? ExtensionSlots
    {
        get => GetValue(ExtensionSlotsProperty);
        set => SetValue(ExtensionSlotsProperty, value);
    }

    public MapViewModel? ViewModel => DataContext as MapViewModel;

    public MapView()
    {
        LoggingWidget.ShowLoggingInMap = ActiveMode.No;

        InitializeComponent();

        PropertyChanged += (_, e) =>
        {
            switch (e.Property.Name)
            {
                case nameof(Timeline):
                    if (e.OldValue is SessionTimelineLinkViewModel oldTimeline)
                    {
                        oldTimeline.PropertyChanged -= OnTimelinePropertyChanged;
                        oldTimeline.VisibleRangeChanged -= OnTimelineVisibleRangeChanged;
                    }

                    if (e.NewValue is SessionTimelineLinkViewModel newTimeline)
                    {
                        newTimeline.PropertyChanged += OnTimelinePropertyChanged;
                        newTimeline.VisibleRangeChanged += OnTimelineVisibleRangeChanged;
                        ApplyTimeline(newTimeline);
                    }

                    break;
                case nameof(ExtensionSlots):
                    SubscribeToSlots(ExtensionSlots);
                    UpdateExtensionMapOverlays();
                    break;
            }
        };

        mapControl = this.FindControl<MapControl>("MapControl");

        // Setup initial layers
        if (mapControl != null)
        {
            mapControl.Map = new Mapsui.Map();
            RemoveLoggingWidgets();
            var trackLayer = CreateFullTrackLayer(); // Initially empty until ViewModel updates
            mapControl.Map.Layers.Add(trackLayer);

            var sessionTrackLayer = CreateSessionTrackLayer(); // Initially empty
            mapControl.Map.Layers.Add(sessionTrackLayer);
            mapControl.Map.Layers.Add(CreateStartEndPointsLayer());
            mapControl.Map.Layers.Add(CreateExtensionOverlayLayer());
            mapControl.Map.Layers.Add(positionMarkerLayer);

            interaction.ViewportNotificationDue += NotifyViewportChanged;
            mapControl.Map.Navigator.ViewportChanged += (_, _) => interaction.NavigatorViewportChanged();
            mapControl.PointerPressed += OnMapPointerPressed;
            mapControl.PointerMoved += OnMapPointerMoved;
            mapControl.PointerReleased += (_, _) => interaction.PointerReleasedOrCaptureLost();
            mapControl.PointerCaptureLost += (_, _) => interaction.PointerReleasedOrCaptureLost();
            mapControl.PointerWheelChanged += (_, _) => interaction.WheelChanged();
        }

        SetNormalizedCursorPosition(1);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToSlots(ExtensionSlots);
        UpdateExtensionMapOverlays();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToSlots(null);
        base.OnDetachedFromVisualTree(e);
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
        else if (e.PropertyName == nameof(MapViewModel.FullTrackPoints)
                 || e.PropertyName == nameof(MapViewModel.SessionTrackPoints)
                 || e.PropertyName == nameof(MapViewModel.TimelineContext))
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateTracks);
        }
    }

    private async void TileProviderComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null || sender is not ComboBox { SelectedItem: TileLayerConfig layer })
        {
            return;
        }

        await ViewModel.SelectLayerAsync(layer);
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

            // Keep late track updates aligned with the shared timeline range.
            if (sessionTrackLayer.Extent != null)
            {
                interaction.RunWithoutViewportTimelineUpdates(() =>
                {
                    if (ViewModel.SessionTrackPoints.Count > 1)
                    {
                        var start = Timeline?.VisibleRangeStart ?? 0;
                        var end = Timeline?.VisibleRangeEnd ?? 1;
                        ZoomToNormalizedRange(start, end);
                    }
                    else
                    {
                        mapControl.Map.Navigator.CenterOnAndZoomTo(sessionTrackLayer.Extent.Centroid, 10);
                    }
                });
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

    private void SubscribeToSlots(RecordedSessionExtensionSlots? slots)
    {
        if (ReferenceEquals(subscribedSlots, slots))
        {
            return;
        }

        if (subscribedSlots is not null)
        {
            subscribedSlots.MapOverlays.CollectionChanged -= OnMapOverlaysChanged;
        }

        subscribedSlots = slots;
        if (subscribedSlots is not null)
        {
            subscribedSlots.MapOverlays.CollectionChanged += OnMapOverlaysChanged;
        }
    }

    private void OnMapOverlaysChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        UpdateExtensionMapOverlays();
    }

    private void UpdateExtensionMapOverlays()
    {
        if (mapControl?.Map.Layers.FindLayer(ExtensionOverlayLayerName).FirstOrDefault() is not MemoryLayer layer)
        {
            return;
        }

        var features = new List<IFeature>();
        if (ExtensionSlots is not null)
        {
            foreach (var contribution in ExtensionSlots.MapOverlays.OrderBy(static contribution => contribution.Order))
            {
                AddExtensionOverlayFeatures(features, contribution);
            }
        }

        layer.Features = features;
        layer.DataHasChanged();
        mapControl.Refresh();
    }

    private void RemoveLoggingWidgets()
    {
        if (mapControl is null)
        {
            return;
        }

        var widgets = mapControl.Map.Widgets;
        if (widgets is ConcurrentQueue<IWidget> widgetQueue)
        {
            var retainedWidgets = widgetQueue
                .Where(static widget => widget is not LoggingWidget)
                .ToArray();
            widgetQueue.Clear();
            foreach (var widget in retainedWidgets)
            {
                widgetQueue.Enqueue(widget);
            }

            return;
        }

        foreach (var widget in mapControl.Map.Widgets.OfType<LoggingWidget>().ToArray())
        {
            if (widgets is ICollection<IWidget> widgetCollection)
            {
                widgetCollection.Remove(widget);
                continue;
            }

            if (widgets is IList widgetList)
            {
                widgetList.Remove(widget);
                continue;
            }

            var removeMethod = widgets.GetType().GetMethod("Remove", [typeof(IWidget)]);
            removeMethod?.Invoke(widgets, [widget]);
        }
    }

    private static void AddExtensionOverlayFeatures(
        List<IFeature> features,
        RecordedSessionMapOverlayContribution contribution)
    {
        foreach (var line in contribution.Lines)
        {
            if (line.Points.Count < 2)
            {
                continue;
            }

            var coordinates = line.Points
                .Select(MapTrackGeometry.ProjectMapCoordinate)
                .Select(point => point.ToCoordinate())
                .ToArray();
            var feature = new GeometryFeature { Geometry = new LineString(coordinates) };
            feature.Styles.Add(new VectorStyle
            {
                Line = new Pen(ToMapColor(line.Style.Color, line.Style.Opacity), line.Style.Width),
            });
            features.Add(feature);
        }

        foreach (var point in contribution.Points)
        {
            var projected = MapTrackGeometry.ProjectMapCoordinate(point.Coordinate);
            var feature = new PointFeature(projected.X, projected.Y);
            feature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill = new Brush(ToMapColor(point.Style.Fill, point.Style.Opacity)),
                Line = new Pen(ToMapColor(point.Style.Stroke, point.Style.Opacity), point.Style.StrokeWidth),
                SymbolScale = point.Style.Radius,
            });
            features.Add(feature);
        }
    }

    private static Color ToMapColor(RecordedSessionMapColor color, double opacity)
    {
        return new Color(
            color.R,
            color.G,
            color.B,
            (int)Math.Round(color.A * Math.Clamp(opacity, 0.0, 1.0)));
    }

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.PreventGestureRecognition();
        interaction.PointerPressed();
    }

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        e.PreventGestureRecognition();
        interaction.PointerMoved();
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

    private static MemoryLayer CreateExtensionOverlayLayer()
    {
        return new MemoryLayer { Name = ExtensionOverlayLayerName };
    }

    public void SetNormalizedCursorPosition(double pos)
    {
        if (pos < 0 || pos > 1)
        {
            return;
        }

        var sessionTrackPoints = ViewModel?.SessionTrackPoints;
        if (sessionTrackPoints is null || sessionTrackPoints.Count == 0)
        {
            ClearNormalizedCursorPosition();
            return;
        }

        var context = GetTimelineContext(sessionTrackPoints);
        if (context is null)
        {
            ClearNormalizedCursorPosition();
            return;
        }

        var targetTime = context.Value.OriginSeconds + pos * context.Value.DurationSeconds;
        var point = MapTrackGeometry.FindClosestTrackPoint(sessionTrackPoints, targetTime);
        if (point is null)
        {
            ClearNormalizedCursorPosition();
            return;
        }

        positionMarkerLayer.Clear();
        var feature = new PointFeature(point.X, point.Y);
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

    private void ClearNormalizedCursorPosition()
    {
        positionMarkerLayer.Clear();
        positionMarkerLayer.DataHasChanged();
        mapControl?.Refresh();
    }

    public void ZoomToNormalizedRange(double startNormalized, double endNormalized, double padding = 0.1)
    {
        var sessionTrackPoints = ViewModel?.SessionTrackPoints;
        if (sessionTrackPoints is null || sessionTrackPoints.Count == 0 || mapControl == null || startNormalized >= endNormalized) return;

        startNormalized = Math.Clamp(startNormalized, 0, 1);
        endNormalized = Math.Clamp(endNormalized, 0, 1);

        var context = GetTimelineContext(sessionTrackPoints);
        if (context is null)
        {
            return;
        }

        var startSeconds = context.Value.OriginSeconds + startNormalized * context.Value.DurationSeconds;
        var endSeconds = context.Value.OriginSeconds + endNormalized * context.Value.DurationSeconds;
        var pointsInRange = MapTrackGeometry.GetTrackPointsInTimeRange(sessionTrackPoints, startSeconds, endSeconds);
        switch (MapViewportController.ComputeRangeFit(pointsInRange, padding))
        {
            case MapViewportController.CenterFit center:
                mapControl.Map.Navigator.CenterOnAndZoomTo(
                    new Mapsui.MPoint(center.X, center.Y),
                    Math.Min(mapControl.Map.Navigator.Viewport.Resolution, center.MaxResolution));
                break;

            case MapViewportController.ExtentFit extent:
                mapControl.Map.Navigator.ZoomToBox(
                    new Mapsui.MRect(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY));
                break;
        }
    }


    private void NotifyViewportChanged()
    {
        var sessionTrackPoints = ViewModel?.SessionTrackPoints;
        if (interaction.IsApplyingTimelineUpdate || sessionTrackPoints is null || sessionTrackPoints.Count < 2 || mapControl == null || Timeline is null) return;

        var viewport = mapControl.Map.Navigator.Viewport;
        var bounds = MapViewportController.ComputeBounds(
            viewport.CenterX,
            viewport.CenterY,
            viewport.Width,
            viewport.Height,
            viewport.Resolution);

        var context = GetTimelineContext(sessionTrackPoints);
        if (context is null ||
            !MapTrackGeometry.TryGetVisibleTrackRange(
                sessionTrackPoints, context.Value, bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY, out var start, out var end))
        {
            return;
        }

        Timeline.SetVisibleRange(start, end, this);
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Timeline is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(SessionTimelineLinkViewModel.NormalizedCursorPosition):
                if (Timeline.NormalizedCursorPosition is double position)
                {
                    SetNormalizedCursorPosition(position);
                }
                else
                {
                    ClearNormalizedCursorPosition();
                }
                break;
        }
    }

    private void OnTimelineVisibleRangeChanged(object? sender, EventArgs e)
    {
        if (Timeline is null || ReferenceEquals(Timeline.VisibleRangeChangeSource, this))
        {
            return;
        }

        ApplyTimeline(Timeline);
    }

    private void ApplyTimeline(SessionTimelineLinkViewModel timeline)
    {
        if (mapControl is null || interaction.IsApplyingTimelineUpdate)
        {
            return;
        }

        interaction.RunWithoutViewportTimelineUpdates(() =>
        {
            if (timeline.NormalizedCursorPosition is double cursor)
            {
                SetNormalizedCursorPosition(cursor);
            }
            else
            {
                ClearNormalizedCursorPosition();
            }

            ZoomToNormalizedRange(timeline.VisibleRangeStart, timeline.VisibleRangeEnd);
        });
    }

    private TrackTimeRange? GetTimelineContext(IReadOnlyList<TrackPoint> sessionTrackPoints)
    {
        return ViewModel?.TimelineContext
               ?? TrackPointSeries.BuildTimelineContext(sessionTrackPoints, originSeconds: null, durationSeconds: null);
    }

    private MemoryLayer CreateSessionTrackLayer()
    {
        var style = new VectorStyle { Line = new Pen(Color.FromString("#9e0142"), 5) };
        return new MemoryLayer { Name = "Session Track", Style = style };
    }
}
