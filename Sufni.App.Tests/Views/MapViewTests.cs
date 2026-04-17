using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
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
}