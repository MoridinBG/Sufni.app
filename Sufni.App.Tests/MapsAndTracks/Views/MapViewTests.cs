using Microsoft.Extensions.Time.Testing;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Mapsui.Layers;
using Sufni.App.Tests.TestSupport.Async;
using Mapsui.Tiling.Layers;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui.UI.Avalonia;
using Mapsui.Widgets.InfoWidgets;
using NSubstitute;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Views;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Extensions;

namespace Sufni.App.Tests.MapsAndTracks.Views;

[Collection("Ui")]
public class MapViewTests
{
    [AvaloniaFact]
    public async Task MapView_BindsAvailableTileLayers_AndCurrentSelection()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var layerA = CreateLayer("Topo");
        var layerB = CreateLayer("Satellite");
        TileLayerConfig[] layers = [layerA, layerB];

        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();
        tileLayerService.Observe().Returns(Observable.Return(new TileLayerState(layers, layerB)));

        using var viewModel = new MapViewModel(tileLayerService, Substitute.For<IMapLayerCoordinator>(), Substitute.For<IDialogService>(), new MapGeometryFactory(new InlineBackgroundTaskRunner()), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider());
        var view = new MapView
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);

        try
        {
            await ViewTestHelpers.FlushDispatcherAsync();

            var comboBox = view.FindControl<ComboBox>("TileProviderComboBox");
            Assert.NotNull(comboBox);
            Assert.Same(layerB, comboBox!.SelectedItem);
            Assert.Equal(layers, comboBox.ItemsSource);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task AddCustomTileButton_ExecutesCommand_AndSelectsReturnedLayer()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var initialLayer = CreateLayer("Topo");
        var customLayer = CreateLayer("Trail Map");
        using var state = new BehaviorSubject<TileLayerState>(new([initialLayer], initialLayer));

        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();
        tileLayerService.Observe().Returns(state);
        var coordinator = Substitute.For<IMapLayerCoordinator>();
        coordinator.AddAndSelectAsync(Arg.Any<TileLayerConfig>()).Returns(_ =>
        {
            state.OnNext(new TileLayerState([initialLayer, customLayer], customLayer));
            return new MapLayerWriteResult.Saved();
        });

        var dialogService = Substitute.For<IDialogService>();
        MapLayerFixture.ConfirmAddDialog(dialogService, customLayer);

        var viewModel = new MapViewModel(tileLayerService, coordinator, dialogService, new MapGeometryFactory(new InlineBackgroundTaskRunner()), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider());
        var view = new MapView
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);

        try
        {
            await ViewTestHelpers.FlushDispatcherAsync();

            var addButton = view.FindControl<Button>("AddCustomTileButton");
            var comboBox = view.FindControl<ComboBox>("TileProviderComboBox");
            Assert.NotNull(addButton);
            Assert.NotNull(comboBox);

            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(addButton!.Command);
            await command.ExecuteAsync(addButton.CommandParameter);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Contains(customLayer, viewModel.AvailableLayers);
            Assert.Same(customLayer, viewModel.SelectedLayer);
            Assert.Same(customLayer, comboBox!.SelectedItem);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task AddCustomTileButton_ShowsFailure_AndKeepsPublishedSelection()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var original = CreateLayer("Topo");
        var added = CreateLayer("Custom");
        var service = Substitute.For<ITileLayerService>();
        service.Observe().Returns(Observable.Return(new TileLayerState([original], original)));
        var coordinator = Substitute.For<IMapLayerCoordinator>();
        coordinator.AddAndSelectAsync(Arg.Any<TileLayerConfig>()).Returns(new MapLayerWriteResult.Failed(MapLayerFailureReason.Add, "disk diagnostic"));
        var dialog = Substitute.For<IDialogService>();
        MapLayerFixture.ConfirmAddDialog(dialog, added);
        using var vm = new MapViewModel(service, coordinator, dialog, new MapGeometryFactory(new InlineBackgroundTaskRunner()), new InlineUiThreadDispatcher(), new FakeTimeProvider());
        var view = new MapView { DataContext = vm };
        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            var errorBar = view.FindControl<ErrorMessagesBar>("MapErrorMessagesBar")!;
            Assert.False(errorBar.IsVisible);
            var button = view.FindControl<Button>("AddCustomTileButton")!;
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(button.Command).ExecuteAsync(button.CommandParameter);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.True(errorBar.IsVisible);
            Assert.Same(original, view.FindControl<ComboBox>("TileProviderComboBox")!.SelectedItem);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task TimelineRangeUpdate_FromExternalSource_ZoomsToTrackSegment()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var viewModel = CreateViewModelWithTrack();
        var timeline = new SessionTimelineLinkViewModel();
        var view = new MapView
        {
            DataContext = viewModel,
            Timeline = timeline,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var mapControl = view.FindControl<ContentControl>("MapHost")!.Content as MapControl;
            Assert.NotNull(mapControl);

            var before = mapControl!.Map.Navigator.Viewport;
            timeline.SetVisibleRange(0.25, 0.5, new object());
            await ViewTestHelpers.FlushDispatcherAsync();

            var after = mapControl.Map.Navigator.Viewport;

            Assert.True(after.Resolution < before.Resolution);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task TimelineRangeUpdate_AfterTrackPointsArrive_ZoomsToCurrentTimelineRange()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();
        var viewModel = new MapViewModel(tileLayerService, Substitute.For<IMapLayerCoordinator>(), Substitute.For<IDialogService>(), new MapGeometryFactory(new InlineBackgroundTaskRunner()), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider())
        {
            FullTrackPoints = [],
        };
        var timeline = new SessionTimelineLinkViewModel();
        var view = new MapView
        {
            DataContext = viewModel,
            Timeline = timeline,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var mapControl = view.FindControl<ContentControl>("MapHost")!.Content as MapControl;
            Assert.NotNull(mapControl);

            timeline.SetVisibleRange(0.25, 0.5, new object());
            viewModel.SessionTrackPoints =
            [
                new TrackPoint(0, 0, 0, null),
                new TrackPoint(1, 100, 100, null),
                new TrackPoint(2, 200, 200, null),
                new TrackPoint(3, 300, 300, null),
                new TrackPoint(4, 400, 400, null),
            ];
            await ViewTestHelpers.FlushDispatcherAsync();

            var segmentViewport = mapControl!.Map.Navigator.Viewport;
            view.ZoomToNormalizedRange(0, 1);
            await ViewTestHelpers.FlushDispatcherAsync();
            var fullViewport = mapControl.Map.Navigator.Viewport;

            Assert.True(segmentViewport.Resolution < fullViewport.Resolution);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task DataContextChange_UnsubscribesPreviousViewModel()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var oldViewModel = CreateViewModelWithTrack();
        var newViewModel = CreateViewModelWithTrack();
        var timeline = new SessionTimelineLinkViewModel();
        var view = new MapView
        {
            DataContext = oldViewModel,
            Timeline = timeline,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var mapControl = view.FindControl<ContentControl>("MapHost")!.Content as MapControl;
            Assert.NotNull(mapControl);

            view.DataContext = newViewModel;
            timeline.SetVisibleRange(0.25, 0.5, new object());
            await ViewTestHelpers.FlushDispatcherAsync();

            view.ZoomToNormalizedRange(0, 1);
            await ViewTestHelpers.FlushDispatcherAsync();
            var fullViewport = mapControl!.Map.Navigator.Viewport;

            oldViewModel.SessionTrackPoints =
            [
                new TrackPoint(0, 0, 0, null),
                new TrackPoint(1, 1_000, 1_000, null),
                new TrackPoint(2, 2_000, 2_000, null),
                new TrackPoint(3, 3_000, 3_000, null),
            ];
            await ViewTestHelpers.FlushDispatcherAsync();

            var afterOldViewModelChange = mapControl.Map.Navigator.Viewport;
            Assert.Equal(fullViewport.CenterX, afterOldViewModelChange.CenterX, 6);
            Assert.Equal(fullViewport.CenterY, afterOldViewModelChange.CenterY, 6);
            Assert.Equal(fullViewport.Resolution, afterOldViewModelChange.Resolution, 6);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task TimelineRangeUpdate_ForStraightTrackSegment_ZoomsToTrackSegment()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var viewModel = CreateViewModelWithHorizontalTrack();
        var timeline = new SessionTimelineLinkViewModel();
        var view = new MapView
        {
            DataContext = viewModel,
            Timeline = timeline,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var mapControl = view.FindControl<ContentControl>("MapHost")!.Content as MapControl;
            Assert.NotNull(mapControl);

            var before = mapControl!.Map.Navigator.Viewport;
            timeline.SetVisibleRange(0.25, 0.5, new object());
            await ViewTestHelpers.FlushDispatcherAsync();

            var after = mapControl.Map.Navigator.Viewport;

            Assert.True(after.Resolution < before.Resolution);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task TimelineRangeUpdate_FromMapSource_DoesNotReapplyToMap()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var viewModel = CreateViewModelWithTrack();
        var timeline = new SessionTimelineLinkViewModel();
        var view = new MapView
        {
            DataContext = viewModel,
            Timeline = timeline,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var mapControl = view.FindControl<ContentControl>("MapHost")!.Content as MapControl;
            Assert.NotNull(mapControl);

            var before = mapControl!.Map.Navigator.Viewport;
            timeline.SetVisibleRange(0.25, 0.5, view);
            await ViewTestHelpers.FlushDispatcherAsync();

            var after = mapControl.Map.Navigator.Viewport;

            Assert.Equal(before.CenterX, after.CenterX, 6);
            Assert.Equal(before.CenterY, after.CenterY, 6);
            Assert.Equal(before.Resolution, after.Resolution, 6);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task MapView_RendersMatchedTrack_WhileOneIsSet()
    {
        ViewTestHelpers.EnsureViewTestResources();

        using var viewModel = CreateViewModelWithTrack();
        var view = new MapView { DataContext = viewModel };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var mapControl = view.FindControl<ContentControl>("MapHost")!.Content as MapControl;
            Assert.NotNull(mapControl);
            var matchedTrackLayer = Assert.Single(
                mapControl!.Map.Layers.FindLayer("Matched Track").OfType<MemoryLayer>());
            Assert.Empty(matchedTrackLayer.Features);

            view.MatchedTrack = new MatchedTrackOverlay(
                [new TrackPoint(0, 0, 0, null), new TrackPoint(1, 10, 10, null), new TrackPoint(2, 20, 20, null)],
                [new TrackPoint(1, 10, 10, null), new TrackPoint(2, 20, 20, null)]);
            await ViewTestHelpers.FlushDispatcherAsync();

            var lines = matchedTrackLayer.Features.Cast<Mapsui.Nts.GeometryFeature>()
                .Select(feature => Assert.IsType<NetTopologySuite.Geometries.LineString>(feature.Geometry))
                .ToArray();
            Assert.Equal(new[] { 0.0, 10.0, 20.0 }, lines[0].Coordinates.Select(point => point.X));
            Assert.Equal(new[] { 10.0, 20.0 }, lines[1].Coordinates.Select(point => point.X));

            view.MatchedTrack = null;
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Empty(matchedTrackLayer.Features);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task MapView_RemovesMapsuiLoggingWidget()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new MapView
        {
            DataContext = CreateViewModelWithTrack(),
        };

        var host = ViewTestHelpers.ShowView(view);

        try
        {
            await ViewTestHelpers.FlushDispatcherAsync();

            var mapControl = view.FindControl<ContentControl>("MapHost")!.Content as MapControl;
            Assert.NotNull(mapControl);
            Assert.Empty(mapControl!.Map.Widgets.OfType<LoggingWidget>());
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task Detach_DisposesLayersAndReattachCreatesFreshMap()
    {
        ViewTestHelpers.EnsureViewTestResources();
        using var viewModel = CreateViewModelWithTrack();
        var view = new MapView { DataContext = viewModel };
        await using var mounted = await ViewTestHelpers.MountAsync(view);
        var original = Assert.IsType<MapControl>(view.FindControl<ContentControl>("MapHost")!.Content);
        var layer = new TrackingTileLayer();
        original.Map.Layers.Add(layer);

        mounted.Host.Content = null;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(layer.IsDisposed);
        Assert.Null(view.FindControl<ContentControl>("MapHost")!.Content);
        mounted.Host.Content = view;
        await ViewTestHelpers.FlushDispatcherAsync();

        var replacement = Assert.IsType<MapControl>(view.FindControl<ContentControl>("MapHost")!.Content);
        Assert.NotSame(original, replacement);
        Assert.Single(Assert.Single(replacement.Map.Layers.FindLayer("Session Track").OfType<MemoryLayer>()).Features);
    }

    [AvaloniaFact]
    public async Task TileSelection_DisposesPreviousTileLayer()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var first = CreateLayer("First");
        using var state = new BehaviorSubject<TileLayerState>(new([first], first));
        var tiles = Substitute.For<ITileLayerService>();
        tiles.Observe().Returns(state);
        using var viewModel = new MapViewModel(tiles, Substitute.For<IMapLayerCoordinator>(), Substitute.For<IDialogService>(),
            new MapGeometryFactory(new InlineBackgroundTaskRunner()), new InlineUiThreadDispatcher(), new FakeTimeProvider());
        var view = new MapView { DataContext = viewModel };
        await using var mounted = await ViewTestHelpers.MountAsync(view);
        var map = Assert.IsType<MapControl>(view.FindControl<ContentControl>("MapHost")!.Content).Map;
        var previous = new TrackingTileLayer();
        map.Layers.Add(previous);
        var next = CreateLayer("Next");

        state.OnNext(new([first, next], next));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(previous.IsDisposed);
        Assert.Equal(next.Name, Assert.Single(map.Layers.OfType<TileLayer>()).Name);
    }

    [AvaloniaFact]
    public async Task GeometryPreparation_RejectsSupersededAndDetachedResults()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var pending = new List<(MapGeometryInput Input, CancellationToken Token, TaskCompletionSource<MapGeometrySnapshot> Completion)>();
        var factory = Substitute.For<IMapGeometryFactory>();
        factory.BuildAsync(Arg.Any<MapGeometryInput>(), Arg.Any<MapGeometrySnapshot?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var completion = new TaskCompletionSource<MapGeometrySnapshot>();
                pending.Add((call.Arg<MapGeometryInput>(), call.Arg<CancellationToken>(), completion));
                return completion.Task;
            });
        using var viewModel = CreateViewModelWithTrack(factory);
        var view = new MapView { DataContext = viewModel };
        var host = await ViewTestHelpers.ShowViewAsync(view);
        var map = Assert.IsType<MapControl>(view.FindControl<ContentControl>("MapHost")!.Content).Map;
        var layer = Assert.Single(map.Layers.FindLayer("Session Track").OfType<MemoryLayer>());
        Assert.Empty(layer.Features);
        viewModel.SessionTrackPoints = [new(0, 500, 500, null), new(1, 600, 600, null)];
        await ViewTestHelpers.FlushDispatcherAsync();
        var builder = new MapGeometryFactory(new InlineBackgroundTaskRunner());
        var current = await builder.BuildAsync(pending[1].Input, null, TestContext.Current.CancellationToken);
        pending[1].Completion.SetResult(current);
        await ViewTestHelpers.FlushDispatcherAsync();
        var line = Assert.IsType<NetTopologySuite.Geometries.LineString>(
            Assert.IsType<Mapsui.Nts.GeometryFeature>(Assert.Single(layer.Features)).Geometry);
        Assert.Equal(new[] { 500.0, 600.0 }, line.Coordinates.Select(point => point.X));
        Assert.Equal(new[] { 500.0, 600.0 }, line.Coordinates.Select(point => point.Y));

        pending[0].Completion.SetResult(await builder.BuildAsync(pending[0].Input, null, TestContext.Current.CancellationToken));
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(pending[0].Token.IsCancellationRequested);
        var afterOldCompletion = Assert.IsType<NetTopologySuite.Geometries.LineString>(
            Assert.IsType<Mapsui.Nts.GeometryFeature>(Assert.Single(layer.Features)).Geometry);
        Assert.Equal(new[] { 500.0, 600.0 }, afterOldCompletion.Coordinates.Select(point => point.X));
        Assert.Equal(new[] { 500.0, 600.0 }, afterOldCompletion.Coordinates.Select(point => point.Y));
        viewModel.SessionTrackPoints = [];
        await ViewTestHelpers.FlushDispatcherAsync();
        host.Close();
        Assert.True(pending[2].Token.IsCancellationRequested);
        pending[2].Completion.SetResult(await builder.BuildAsync(pending[2].Input, null, TestContext.Current.CancellationToken));
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Null(view.FindControl<ContentControl>("MapHost")!.Content);
    }

    [AvaloniaFact]
    public async Task GeometryFailure_ShowsFixedFeedbackWithSeparateDiagnostics()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var factory = Substitute.For<IMapGeometryFactory>();
        factory.BuildAsync(Arg.Any<MapGeometryInput>(), Arg.Any<MapGeometrySnapshot?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MapGeometrySnapshot>(new InvalidOperationException("geometry diagnostic")));
        using var viewModel = CreateViewModelWithTrack(factory);
        var view = new MapView { DataContext = viewModel };
        await using var mounted = await ViewTestHelpers.MountAsync(view);

        Assert.True(view.FindControl<ErrorMessagesBar>("MapErrorMessagesBar")!.IsVisible);
        var failure = Assert.Single(viewModel.ErrorMessages);
        Assert.Equal("geometry diagnostic", failure.Details);
        Assert.DoesNotContain(failure.Details, failure.Summary);
    }

    private sealed class TrackingTileLayer() : TileLayer(new HttpTileSource(new GlobalSphericalMercator(), "https://example.com/{z}/{x}/{y}.png"))
    {
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private static TileLayerConfig CreateLayer(string name) => new()
    {
        Name = name,
        UrlTemplate = "https://example.com/{z}/{x}/{y}.png",
        AttributionText = "Example",
        AttributionUrl = "https://example.com",
        MaxZoom = 18,
    };

    private static MapViewModel CreateViewModelWithTrack(IMapGeometryFactory? geometryFactory = null)
    {
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();

        return new MapViewModel(tileLayerService, Substitute.For<IMapLayerCoordinator>(), Substitute.For<IDialogService>(), geometryFactory ?? new MapGeometryFactory(new InlineBackgroundTaskRunner()), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider())
        {
            SessionTrackPoints =
            [
                new TrackPoint(0, 0, 0, null),
                new TrackPoint(1, 100, 100, null),
                new TrackPoint(2, 200, 200, null),
                new TrackPoint(3, 300, 300, null),
                new TrackPoint(4, 400, 400, null),
            ],
            FullTrackPoints = [],
        };
    }

    private static MapViewModel CreateViewModelWithHorizontalTrack()
    {
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();

        return new MapViewModel(tileLayerService, Substitute.For<IMapLayerCoordinator>(), Substitute.For<IDialogService>(), new MapGeometryFactory(new InlineBackgroundTaskRunner()), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider())
        {
            SessionTrackPoints =
            [
                new TrackPoint(0, 0, 0, null),
                new TrackPoint(1, 100, 0, null),
                new TrackPoint(2, 200, 0, null),
                new TrackPoint(3, 300, 0, null),
                new TrackPoint(4, 400, 0, null),
            ],
            FullTrackPoints = [],
        };
    }
}
