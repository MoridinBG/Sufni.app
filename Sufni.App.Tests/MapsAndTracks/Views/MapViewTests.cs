using Microsoft.Extensions.Time.Testing;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Mapsui.Layers;
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

        var viewModel = new MapViewModel(tileLayerService, Substitute.For<IMapLayerCoordinator>(), Substitute.For<IDialogService>(), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider());
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
            Assert.Contains(layerA, layers);
            Assert.Contains(layerB, layers);
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
        coordinator.AddAndSelectAsync(customLayer).Returns(_ =>
        {
            state.OnNext(new TileLayerState([initialLayer, customLayer], customLayer));
            return new MapLayerWriteResult.Saved();
        });

        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowAddTileLayerDialogAsync().Returns(Task.FromResult<TileLayerConfig?>(customLayer));

        var viewModel = new MapViewModel(tileLayerService, coordinator, dialogService, new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider());
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
        coordinator.AddAndSelectAsync(added).Returns(new MapLayerWriteResult.Failed(MapLayerFailureReason.Add, "disk diagnostic"));
        var dialog = Substitute.For<IDialogService>();
        dialog.ShowAddTileLayerDialogAsync().Returns(added);
        using var vm = new MapViewModel(service, coordinator, dialog, new InlineUiThreadDispatcher(), new FakeTimeProvider());
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
    public async Task TimelineCursorUpdate_AcceptsEmptySessionTrackPoints()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();

        var viewModel = new MapViewModel(tileLayerService, Substitute.For<IMapLayerCoordinator>(), Substitute.For<IDialogService>(), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider())
        {
            SessionTrackPoints = []
        };
        var timeline = new SessionTimelineLinkViewModel();
        var view = new MapView
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);

        try
        {
            await ViewTestHelpers.FlushDispatcherAsync();

            view.Timeline = timeline;
            timeline.SetCursorPosition(0.5);

            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(0.5, timeline.NormalizedCursorPosition);
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
            var mapControl = view.FindControl<MapControl>("MapControl");
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
        var viewModel = new MapViewModel(tileLayerService, Substitute.For<IMapLayerCoordinator>(), Substitute.For<IDialogService>(), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider())
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
            var mapControl = view.FindControl<MapControl>("MapControl");
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
            var mapControl = view.FindControl<MapControl>("MapControl");
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
            var mapControl = view.FindControl<MapControl>("MapControl");
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
            var mapControl = view.FindControl<MapControl>("MapControl");
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
    public async Task TimelineContextUpdate_ReusesRenderedTrackGeometry()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var viewModel = CreateViewModelWithTrack();
        viewModel.TimelineContext = new TrackTimeRange(0, 4);
        var view = new MapView
        {
            DataContext = viewModel,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var mapControl = view.FindControl<MapControl>("MapControl");
            Assert.NotNull(mapControl);

            var sessionLayer = Assert.Single(
                mapControl!.Map.Layers.FindLayer("Session Track").OfType<MemoryLayer>());
            var markerLayer = Assert.Single(
                mapControl.Map.Layers.FindLayer("Start/End Marker").OfType<MemoryLayer>());
            var sessionFeature = Assert.Single(sessionLayer.Features);
            var markerFeatures = markerLayer.Features.ToArray();
            Assert.Equal(2, markerFeatures.Length);

            viewModel.TimelineContext = new TrackTimeRange(1, 2);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Same(sessionFeature, Assert.Single(sessionLayer.Features));
            Assert.Equal(markerFeatures, markerLayer.Features.ToArray());
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task CursorMove_ReusesRenderedTrackGeometry()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var viewModel = CreateViewModelWithTrack();
        viewModel.TimelineContext = new TrackTimeRange(0, 4);
        var view = new MapView
        {
            DataContext = viewModel,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var mapControl = view.FindControl<MapControl>("MapControl");
            Assert.NotNull(mapControl);

            var sessionLayer = Assert.Single(
                mapControl!.Map.Layers.FindLayer("Session Track").OfType<MemoryLayer>());
            var sessionFeature = Assert.Single(sessionLayer.Features);

            view.SetNormalizedCursorPosition(0.25);
            view.SetNormalizedCursorPosition(0.75);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Same(sessionFeature, Assert.Single(sessionLayer.Features));
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

        var view = new MapView { DataContext = CreateViewModelWithTrack() };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var mapControl = view.FindControl<MapControl>("MapControl");
            Assert.NotNull(mapControl);
            var matchedTrackLayer = Assert.Single(
                mapControl!.Map.Layers.FindLayer("Matched Track").OfType<MemoryLayer>());
            Assert.Empty(matchedTrackLayer.Features);

            view.MatchedTrack = new MatchedTrackOverlay(
                [new TrackPoint(0, 0, 0, null), new TrackPoint(1, 10, 10, null), new TrackPoint(2, 20, 20, null)],
                [new TrackPoint(1, 10, 10, null), new TrackPoint(2, 20, 20, null)]);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(2, matchedTrackLayer.Features.Count());

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

            var mapControl = view.FindControl<MapControl>("MapControl");
            Assert.NotNull(mapControl);
            Assert.Empty(mapControl!.Map.Widgets.OfType<LoggingWidget>());
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
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

    private static MapViewModel CreateViewModelWithTrack()
    {
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();

        return new MapViewModel(tileLayerService, Substitute.For<IMapLayerCoordinator>(), Substitute.For<IDialogService>(), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider())
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

        return new MapViewModel(tileLayerService, Substitute.For<IMapLayerCoordinator>(), Substitute.For<IDialogService>(), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider())
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
