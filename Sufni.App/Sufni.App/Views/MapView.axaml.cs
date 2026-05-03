using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Nts.Extensions;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Avalonia;
using Mapsui.Widgets;
using Mapsui.Widgets.InfoWidgets;
using NetTopologySuite.Geometries;
using Sufni.App.Models;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Views;

public partial class MapView : UserControl
{
    private MapControl? mapControl;
    private bool applyingTimelineUpdate;
    private bool mapPointerInteractionActive;
    private bool viewportNotificationQueued;

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

    public MapViewModel? ViewModel => DataContext as MapViewModel;

    public MapView()
    {
        LoggingWidget.ShowLoggingInMap = ActiveMode.No;

        InitializeComponent();

        PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != nameof(Timeline))
            {
                return;
            }

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
        };

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

            mapControl.Map.Navigator.ViewportChanged += OnNavigatorViewportChanged;
            mapControl.PointerPressed += OnMapPointerPressed;
            mapControl.PointerMoved += OnMapPointerMoved;
            mapControl.PointerReleased += (_, _) =>
            {
                QueueViewportChangedNotification();
                mapPointerInteractionActive = false;
            };
            mapControl.PointerCaptureLost += (_, _) =>
            {
                QueueViewportChangedNotification();
                mapPointerInteractionActive = false;
            };
            mapControl.PointerWheelChanged += (_, _) => QueueViewportChangedNotification();
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
        else if (e.PropertyName == nameof(MapViewModel.FullTrackPoints)
                 || e.PropertyName == nameof(MapViewModel.SessionTrackPoints)
                 || e.PropertyName == nameof(MapViewModel.TimelineContext))
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
                RunWithoutViewportTimelineUpdates(() =>
                {
                    if (ViewModel.SessionTrackPoints.Count > 1)
                    {
                        ZoomToNormalizedRange(0, 1);
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

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.PreventGestureRecognition();
        mapPointerInteractionActive = true;
    }

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        e.PreventGestureRecognition();
        if (mapPointerInteractionActive)
        {
            QueueViewportChangedNotification();
        }
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
        var point = FindClosestTrackPoint(sessionTrackPoints, targetTime);
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
        var pointsInRange = GetTrackPointsInTimeRange(sessionTrackPoints, startSeconds, endSeconds);
        if (pointsInRange.Count == 0)
        {
            return;
        }

        var minX = pointsInRange.Min(p => p.X);
        var maxX = pointsInRange.Max(p => p.X);
        var minY = pointsInRange.Min(p => p.Y);
        var maxY = pointsInRange.Max(p => p.Y);

        var width = maxX - minX;
        var height = maxY - minY;
        if (width <= 0 || height <= 0)
        {
            mapControl.Map.Navigator.CenterOnAndZoomTo(
                new Mapsui.MPoint((minX + maxX) / 2, (minY + maxY) / 2),
                mapControl.Map.Navigator.Viewport.Resolution);
            return;
        }

        var paddingX = width * padding;
        var paddingY = height * padding;

        var extent = new Mapsui.MRect(
            minX - paddingX,
            minY - paddingY,
            maxX + paddingX,
            maxY + paddingY);

        mapControl.Map.Navigator.ZoomToBox(extent);
    }

    private void OnNavigatorViewportChanged(object? sender, EventArgs e)
    {
        if (mapPointerInteractionActive)
        {
            QueueViewportChangedNotification();
        }
    }

    private void QueueViewportChangedNotification()
    {
        if (viewportNotificationQueued || applyingTimelineUpdate)
        {
            return;
        }

        viewportNotificationQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            viewportNotificationQueued = false;
            NotifyViewportChanged();
        }, DispatcherPriority.Background);
    }

    private void NotifyViewportChanged()
    {
        var sessionTrackPoints = ViewModel?.SessionTrackPoints;
        if (applyingTimelineUpdate || sessionTrackPoints is null || sessionTrackPoints.Count < 2 || mapControl == null || Timeline is null) return;

        var viewport = mapControl.Map.Navigator.Viewport;
        var halfWidth = viewport.Width * viewport.Resolution / 2;
        var halfHeight = viewport.Height * viewport.Resolution / 2;
        var minX = viewport.CenterX - halfWidth;
        var maxX = viewport.CenterX + halfWidth;
        var minY = viewport.CenterY - halfHeight;
        var maxY = viewport.CenterY + halfHeight;

        var context = GetTimelineContext(sessionTrackPoints);
        if (context is null ||
            !TryGetVisibleTrackRange(sessionTrackPoints, context.Value, minX, maxX, minY, maxY, out var start, out var end))
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
        if (mapControl is null || applyingTimelineUpdate)
        {
            return;
        }

        applyingTimelineUpdate = true;
        try
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
        }
        finally
        {
            applyingTimelineUpdate = false;
        }
    }

    private void RunWithoutViewportTimelineUpdates(Action action)
    {
        if (applyingTimelineUpdate)
        {
            action();
            return;
        }

        applyingTimelineUpdate = true;
        try
        {
            action();
        }
        finally
        {
            applyingTimelineUpdate = false;
        }
    }

    private static bool TryGetVisibleTrackRange(
        IReadOnlyList<TrackPoint> sessionTrackPoints,
        TrackTimelineContext context,
        double minX,
        double maxX,
        double minY,
        double maxY,
        out double start,
        out double end)
    {
        var firstVisible = -1;
        var lastVisible = -1;

        for (var i = 0; i < sessionTrackPoints.Count; i++)
        {
            var point = sessionTrackPoints[i];
            if (IsPointVisible(point, minX, maxX, minY, maxY))
            {
                if (firstVisible == -1) firstVisible = i;
                lastVisible = i;
            }

            if (i == 0 || !SegmentIntersectsViewport(sessionTrackPoints[i - 1], point, minX, maxX, minY, maxY))
            {
                continue;
            }

            if (firstVisible == -1) firstVisible = i - 1;
            lastVisible = i;
        }

        if (firstVisible < 0 || lastVisible <= firstVisible)
        {
            start = 0;
            end = 1;
            return false;
        }

        var firstTime = sessionTrackPoints[firstVisible].Time;
        var lastTime = sessionTrackPoints[lastVisible].Time;
        if (!double.IsFinite(firstTime) || !double.IsFinite(lastTime))
        {
            start = 0;
            end = 1;
            return false;
        }

        start = NormalizeTime(firstTime, context);
        end = NormalizeTime(lastTime, context);
        return true;
    }

    private TrackTimelineContext? GetTimelineContext(IReadOnlyList<TrackPoint> sessionTrackPoints)
    {
        return ViewModel?.TimelineContext
               ?? TrackPointSeries.BuildTimelineContext(sessionTrackPoints, originSeconds: null, durationSeconds: null);
    }

    private static TrackPoint? FindClosestTrackPoint(IReadOnlyList<TrackPoint> sessionTrackPoints, double targetTime)
    {
        TrackPoint? closest = null;
        var closestDistance = double.PositiveInfinity;

        foreach (var point in sessionTrackPoints)
        {
            if (!double.IsFinite(point.Time))
            {
                continue;
            }

            var distance = Math.Abs(point.Time - targetTime);
            if (distance >= closestDistance)
            {
                continue;
            }

            closest = point;
            closestDistance = distance;
        }

        return closest;
    }

    private static List<TrackPoint> GetTrackPointsInTimeRange(
        IReadOnlyList<TrackPoint> sessionTrackPoints,
        double startSeconds,
        double endSeconds)
    {
        var pointsInRange = new List<TrackPoint>();
        TrackPoint? before = null;
        TrackPoint? after = null;

        foreach (var point in sessionTrackPoints)
        {
            if (!double.IsFinite(point.Time))
            {
                continue;
            }

            if (point.Time < startSeconds)
            {
                before = point;
                continue;
            }

            if (point.Time > endSeconds)
            {
                after ??= point;
                continue;
            }

            pointsInRange.Add(point);
        }

        if (before is not null)
        {
            pointsInRange.Insert(0, before);
        }

        if (after is not null)
        {
            pointsInRange.Add(after);
        }

        return pointsInRange;
    }

    private static double NormalizeTime(double timeSeconds, TrackTimelineContext context)
    {
        return Math.Clamp((timeSeconds - context.OriginSeconds) / context.DurationSeconds, 0, 1);
    }

    private static bool IsPointVisible(TrackPoint point, double minX, double maxX, double minY, double maxY)
    {
        return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
    }

    private static bool SegmentIntersectsViewport(
        TrackPoint start,
        TrackPoint end,
        double minX,
        double maxX,
        double minY,
        double maxY)
    {
        var x0 = start.X;
        var y0 = start.Y;
        var dx = end.X - x0;
        var dy = end.Y - y0;
        var entering = 0d;
        var leaving = 1d;

        return ClipSegment(-dx, x0 - minX, ref entering, ref leaving)
            && ClipSegment(dx, maxX - x0, ref entering, ref leaving)
            && ClipSegment(-dy, y0 - minY, ref entering, ref leaving)
            && ClipSegment(dy, maxY - y0, ref entering, ref leaving);
    }

    private static bool ClipSegment(double direction, double distance, ref double entering, ref double leaving)
    {
        if (Math.Abs(direction) <= 0.000000001)
        {
            return distance >= 0;
        }

        var ratio = distance / direction;
        if (direction < 0)
        {
            if (ratio > leaving) return false;
            if (ratio > entering) entering = ratio;
        }
        else
        {
            if (ratio < entering) return false;
            if (ratio < leaving) leaving = ratio;
        }

        return true;
    }

    private MemoryLayer CreateSessionTrackLayer()
    {
        var style = new VectorStyle { Line = new Pen(Color.FromString("#9e0142"), 5) };
        return new MemoryLayer { Name = "Session Track", Style = style };
    }
}
