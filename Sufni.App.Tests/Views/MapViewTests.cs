using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views;

namespace Sufni.App.Tests.Views;

public class MapViewTests
{
    [AvaloniaFact]
    public async Task MapView_BindsAvailableTileLayers_AndCurrentSelection()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var layerA = CreateLayer("Topo");
        var layerB = CreateLayer("Satellite");
        var layers = new ObservableCollection<TileLayerConfig> { layerA, layerB };

        var tileLayerService = Substitute.For<ITileLayerService>();
        tileLayerService.AvailableLayers.Returns(layers);

        var viewModel = new MapViewModel(tileLayerService, Substitute.For<IDialogService>())
        {
            SelectedLayer = layerB,
        };
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
            Assert.Equal(2, layers.Count);
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
        var layers = new ObservableCollection<TileLayerConfig> { initialLayer };

        var tileLayerService = Substitute.For<ITileLayerService>();
        tileLayerService.AvailableLayers.Returns(layers);
        tileLayerService.AddCustomLayerAsync(customLayer).Returns(_ =>
        {
            layers.Add(customLayer);
            return Task.CompletedTask;
        });

        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowAddTileLayerDialogAsync().Returns(Task.FromResult<TileLayerConfig?>(customLayer));

        var viewModel = new MapViewModel(tileLayerService, dialogService)
        {
            SelectedLayer = initialLayer,
        };
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

            Assert.Contains(customLayer, layers);
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
    public async Task TimelineCursorUpdate_AcceptsEmptySessionTrackPoints()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var tileLayerService = Substitute.For<ITileLayerService>();
        tileLayerService.AvailableLayers.Returns(new ObservableCollection<TileLayerConfig>());

        var viewModel = new MapViewModel(tileLayerService, Substitute.For<IDialogService>())
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

    private static TileLayerConfig CreateLayer(string name) => new()
    {
        Name = name,
        UrlTemplate = "https://example.com/{z}/{x}/{y}.png",
        AttributionText = "Example",
        AttributionUrl = "https://example.com",
        MaxZoom = 18,
    };
}