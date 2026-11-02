using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
using Sufni.App.MapsAndTracks.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Avalonia;
using Mapsui.Utilities;
using Mapsui.Widgets;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
namespace Sufni.App.MapsAndTracks.Views;

public partial class MapView : UserControl
{
    private const string MatchedTrackLayerName = "Matched Track";
    private const string MapsuiInfoWidgetNamespace = "Mapsui.Widgets.InfoWidgets";

    private MapControl? mapControl;
    private readonly MapInteractionController interaction = new(
        action => Dispatcher.UIThread.Post(action, DispatcherPriority.Background));
    private static readonly ILogger logger = Log.ForContext<MapView>();
    private readonly CancellableOperation geometryOperation = new();
    private MapGeometrySnapshot? geometry;
    private MapGeometryInput? requestedGeometry;
    private SessionTimelineLinkViewModel? subscribedTimeline;
    private MapViewModel? subscribedViewModel;
    private RenderedTrackGeometry? appliedFullTrackGeometry;
    private RenderedTrackGeometry? appliedSessionTrackGeometry;
    private RenderedTrackGeometry? appliedMarkerTrackGeometry;

    private WritableLayer? positionMarkerLayer;

    public static readonly StyledProperty<SessionTimelineLinkViewModel?> TimelineProperty =
        AvaloniaProperty.Register<MapView, SessionTimelineLinkViewModel?>(nameof(Timeline));

    public SessionTimelineLinkViewModel? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly StyledProperty<MatchedTrackOverlay?> MatchedTrackProperty =
        AvaloniaProperty.Register<MapView, MatchedTrackOverlay?>(nameof(MatchedTrack));

    public MatchedTrackOverlay? MatchedTrack
    {
        get => GetValue(MatchedTrackProperty);
        set => SetValue(MatchedTrackProperty, value);
    }

    public MapViewModel? ViewModel => DataContext as MapViewModel;

    public MapView()
    {
        InitializeComponent();

        PropertyChanged += (_, e) =>
        {
            switch (e.Property.Name)
            {
                case nameof(Timeline):
                    if (VisualRoot is not null)
                    {
                        SubscribeTimeline(Timeline);
                        if (Timeline is not null) ApplyTimeline(Timeline);
                    }

                    break;
                case nameof(MatchedTrack):
                    UpdateTracks();
                    break;
            }
        };

        interaction.ViewportNotificationDue += NotifyViewportChanged;
    }

    private void CreateMap()
    {
        mapControl = new MapControl();
        // Mapsui's MapControl constructor stores a process-lifetime static delegate
        // (Mapsui.Utilities.PlatformUtilities.OpenInBrowserMethod) bound to the control
        // instance, and never clears it on dispose. That static pins the most recently
        // created MapControl - and through the visual tree the whole session view and its
        // telemetry - until another map is created. Re-point it at a control-independent
        // handler so the control's lifetime is governed by the visual tree alone.
        PlatformUtilities.SetOpenInBrowserFunc(static url =>
        {
            using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
        });

        // Setup initial layers
        if (mapControl != null)
        {
            RemoveInfoWidgets();
            var trackLayer = CreateFullTrackLayer(); // Initially empty until ViewModel updates
            mapControl.Map.Layers.Add(trackLayer);

            var sessionTrackLayer = CreateSessionTrackLayer(); // Initially empty
            mapControl.Map.Layers.Add(sessionTrackLayer);
            mapControl.Map.Layers.Add(CreateStartEndPointsLayer());
            mapControl.Map.Layers.Add(new MemoryLayer { Name = MatchedTrackLayerName });
            positionMarkerLayer = new WritableLayer { Name = "Position Marker", Style = new SymbolStyle { SymbolScale = 0.5 } };
            mapControl.Map.Layers.Add(positionMarkerLayer);

            mapControl.Map.Navigator.ViewportChanged += OnNavigatorViewportChanged;
            mapControl.PointerPressed += OnMapPointerPressed;
            mapControl.PointerMoved += OnMapPointerMoved;
            mapControl.PointerReleased += (_, _) => interaction.PointerReleasedOrCaptureLost();
            mapControl.PointerCaptureLost += (_, _) => interaction.PointerReleasedOrCaptureLost();
            mapControl.PointerWheelChanged += (_, _) => interaction.WheelChanged();
        }

        MapHost.Content = mapControl;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CreateMap();
        SubscribeTimeline(Timeline);
        SubscribeToViewModel(ViewModel);
        ApplyViewModelState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToViewModel(null);
        SubscribeTimeline(null);
        geometryOperation.Cancel();
        requestedGeometry = null;
        geometry = null;
        appliedFullTrackGeometry = appliedSessionTrackGeometry = appliedMarkerTrackGeometry = null;
        var oldMap = mapControl;
        mapControl = null;
        positionMarkerLayer = null;
        MapHost.Content = null;
        if (oldMap is not null)
        {
            oldMap.Map.Navigator.ViewportChanged -= OnNavigatorViewportChanged;
            foreach (var layer in oldMap.Map.Layers.ToArray())
            {
                oldMap.Map.Layers.Remove(layer);
                layer.Dispose();
            }
            oldMap.Dispose();
        }
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is null) return;
        geometryOperation.Cancel();
        requestedGeometry = null;
        geometry = null;
        appliedFullTrackGeometry = appliedSessionTrackGeometry = appliedMarkerTrackGeometry = null;
        if (mapControl is not null)
        {
            foreach (var layer in mapControl.Map.Layers.OfType<MemoryLayer>())
            {
                layer.Features = [];
                layer.DataHasChanged();
            }
            ClearNormalizedCursorPosition();
            mapControl.Refresh();
        }
        SubscribeToViewModel(ViewModel);
        ApplyViewModelState();
    }

    private void OnNavigatorViewportChanged(object? sender, EventArgs args) => interaction.NavigatorViewportChanged();

    private void SubscribeTimeline(SessionTimelineLinkViewModel? value)
    {
        if (subscribedTimeline is not null)
        {
            subscribedTimeline.PropertyChanged -= OnTimelinePropertyChanged;
            subscribedTimeline.VisibleRangeChanged -= OnTimelineVisibleRangeChanged;
        }
        subscribedTimeline = value;
        if (value is not null)
        {
            value.PropertyChanged += OnTimelinePropertyChanged;
            value.VisibleRangeChanged += OnTimelineVisibleRangeChanged;
        }
    }

    private void SubscribeToViewModel(MapViewModel? viewModel)
    {
        if (ReferenceEquals(subscribedViewModel, viewModel))
        {
            return;
        }

        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        subscribedViewModel = viewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ApplyViewModelState()
    {
        if (subscribedViewModel is null)
        {
            return;
        }

        if (subscribedViewModel.SelectedLayer != null)
            UpdateTileLayer(subscribedViewModel.SelectedLayer);

        UpdateTracks();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, subscribedViewModel) || subscribedViewModel is null)
        {
            return;
        }

        var viewModel = subscribedViewModel;
        if (e.PropertyName == nameof(MapViewModel.SelectedLayer) && viewModel.SelectedLayer != null)
        {
            var layer = viewModel.SelectedLayer;
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(subscribedViewModel, viewModel))
                {
                    UpdateTileLayer(layer);
                }
            });
        }
        else if (e.PropertyName == nameof(MapViewModel.FullTrackPoints)
                 || e.PropertyName == nameof(MapViewModel.SessionTrackPoints)
                 || e.PropertyName == nameof(MapViewModel.TimelineContext))
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(subscribedViewModel, viewModel))
                {
                    UpdateTracks();
                }
            });
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
        if (mapControl is null || ViewModel is not { } viewModel) return;
        var input = new MapGeometryInput(viewModel.FullTrackPoints is { } full ? full : Array.Empty<TrackPoint>(),
            viewModel.SessionTrackPoints is { } session ? session : Array.Empty<TrackPoint>(), MatchedTrack);
        if (input == requestedGeometry)
        {
            if (geometry?.Input == input) ApplyGeometry(geometry);
            return;
        }
        requestedGeometry = input;
        var token = geometryOperation.Start();
        _ = PrepareGeometryAsync(viewModel.GeometryFactory, input, token);
    }

    private async Task PrepareGeometryAsync(IMapGeometryFactory factory, MapGeometryInput input, CancellationToken token)
    {
        try
        {
            var prepared = await factory.BuildAsync(input, geometry, token);
            if (token.IsCancellationRequested || mapControl is null) return;
            geometry = prepared;
            ApplyGeometry(prepared);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested) return;
            logger.Error(exception, "Map geometry preparation failed");
            ViewModel?.ReportRenderingFailure(FailurePresentation.From(MapRenderingFailureReason.Geometry, exception.Message));
        }
    }

    private void ApplyGeometry(MapGeometrySnapshot prepared)
    {
        if (mapControl is null) return;
        // Update Full Track
        var fullTrackLayer = mapControl.Map.Layers.FindLayer("Full Track").FirstOrDefault() as MemoryLayer;
        if (fullTrackLayer != null)
        {
            var fullTrackGeometry = prepared.FullTrack;
            ApplyTrackLayer(fullTrackLayer, fullTrackGeometry, ref appliedFullTrackGeometry);
        }

        RenderedTrackGeometry? sessionTrackGeometry = null;

        // Update Session Track
        var sessionTrackLayer = mapControl.Map.Layers.FindLayer("Session Track").FirstOrDefault() as MemoryLayer;
        if (sessionTrackLayer != null)
        {
            sessionTrackGeometry = prepared.SessionTrack;
            ApplyTrackLayer(sessionTrackLayer, sessionTrackGeometry, ref appliedSessionTrackGeometry);

            // Keep late track updates aligned with the shared timeline range.
            if (sessionTrackLayer.Extent != null)
            {
                interaction.RunWithoutViewportTimelineUpdates(() =>
                {
                    if (sessionTrackGeometry.Points.Count > 1)
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
        if (markerLayer != null && sessionTrackGeometry is not null)
        {
            ApplyMarkerLayer(markerLayer, sessionTrackGeometry, ref appliedMarkerTrackGeometry);
        }

        if (mapControl.Map.Layers.FindLayer(MatchedTrackLayerName).FirstOrDefault() is MemoryLayer matchedLayer)
        {
            matchedLayer.Features = prepared.MatchedFeatures;
            matchedLayer.DataHasChanged();
        }
        if (Timeline?.NormalizedCursorPosition is { } cursor) SetNormalizedCursorPosition(cursor);
        mapControl.Refresh();
    }

    private static void ApplyTrackLayer(
        MemoryLayer layer,
        RenderedTrackGeometry geometry,
        ref RenderedTrackGeometry? appliedGeometry)
    {
        if (ReferenceEquals(appliedGeometry, geometry))
        {
            return;
        }

        layer.Features = geometry.LineFeatures;
        layer.DataHasChanged();
        appliedGeometry = geometry;
    }

    private static void ApplyMarkerLayer(
        MemoryLayer layer,
        RenderedTrackGeometry geometry,
        ref RenderedTrackGeometry? appliedGeometry)
    {
        if (ReferenceEquals(appliedGeometry, geometry))
        {
            return;
        }

        layer.Features = geometry.MarkerFeatures;
        layer.DataHasChanged();
        appliedGeometry = geometry;
    }

    private void RemoveInfoWidgets()
    {
        if (mapControl is null)
        {
            return;
        }

        var widgets = mapControl.Map.Widgets;
        if (widgets is ConcurrentQueue<IWidget> widgetQueue)
        {
            var retainedWidgets = widgetQueue
                .Where(static widget => !IsInfoWidget(widget))
                .ToArray();
            widgetQueue.Clear();
            foreach (var widget in retainedWidgets)
            {
                widgetQueue.Enqueue(widget);
            }

            return;
        }

        foreach (var widget in mapControl.Map.Widgets.Where(IsInfoWidget).ToArray())
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

    private static bool IsInfoWidget(IWidget widget) =>
        widget.GetType().Namespace == MapsuiInfoWidgetNamespace;

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
            layer.Dispose();
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
        if (positionMarkerLayer is null || pos < 0 || pos > 1)
        {
            return;
        }

        var sessionTrackGeometry = GetSessionTrackGeometry();
        if (sessionTrackGeometry is null || sessionTrackGeometry.Points.Count == 0)
        {
            ClearNormalizedCursorPosition();
            return;
        }

        var context = GetTimelineContext(sessionTrackGeometry);
        if (context is null)
        {
            ClearNormalizedCursorPosition();
            return;
        }

        var targetTime = context.Value.OriginSeconds + pos * context.Value.DurationSeconds;
        var timeIndex = sessionTrackGeometry.TimeIndex;
        if (timeIndex is null)
        {
            ClearNormalizedCursorPosition();
            return;
        }

        var point = timeIndex.FindClosest(targetTime);
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
        if (positionMarkerLayer is null) return;
        positionMarkerLayer.Clear();
        positionMarkerLayer.DataHasChanged();
        mapControl?.Refresh();
    }

    public void ZoomToNormalizedRange(double startNormalized, double endNormalized, double padding = 0.1)
    {
        var sessionTrackGeometry = GetSessionTrackGeometry();
        if (sessionTrackGeometry is null || sessionTrackGeometry.Points.Count == 0 || mapControl == null || startNormalized >= endNormalized) return;

        startNormalized = Math.Clamp(startNormalized, 0, 1);
        endNormalized = Math.Clamp(endNormalized, 0, 1);

        var context = GetTimelineContext(sessionTrackGeometry);
        if (context is null)
        {
            return;
        }

        var startSeconds = context.Value.OriginSeconds + startNormalized * context.Value.DurationSeconds;
        var endSeconds = context.Value.OriginSeconds + endNormalized * context.Value.DurationSeconds;
        var timeIndex = sessionTrackGeometry.TimeIndex;
        if (timeIndex is null)
        {
            return;
        }

        var pointsInRange = timeIndex.GetRangeWithBoundaryNeighbors(startSeconds, endSeconds);
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
        var sessionTrackGeometry = GetSessionTrackGeometry();
        if (interaction.IsApplyingTimelineUpdate || sessionTrackGeometry is null || sessionTrackGeometry.Points.Count < 2 || mapControl == null || Timeline is null) return;

        var viewport = mapControl.Map.Navigator.Viewport;
        var bounds = MapViewportController.ComputeBounds(
            viewport.CenterX,
            viewport.CenterY,
            viewport.Width,
            viewport.Height,
            viewport.Resolution);

        var context = GetTimelineContext(sessionTrackGeometry);
        if (context is null ||
            !MapTrackGeometry.TryGetVisibleTrackRange(
                sessionTrackGeometry.Points, context.Value, bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY, out var start, out var end))
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

    private RenderedTrackGeometry? GetSessionTrackGeometry()
    {
        return ReferenceEquals(geometry?.Input.SessionTrack, ViewModel?.SessionTrackPoints) ? geometry?.SessionTrack : null;
    }

    private TrackTimeRange? GetTimelineContext(RenderedTrackGeometry sessionTrackGeometry)
    {
        return ViewModel?.TimelineContext
               ?? sessionTrackGeometry.TimeIndex?.TimelineContext;
    }

    private MemoryLayer CreateSessionTrackLayer()
    {
        var style = new VectorStyle { Line = new Pen(Color.FromString("#9e0142"), 5) };
        return new MemoryLayer { Name = "Session Track", Style = style };
    }
}
