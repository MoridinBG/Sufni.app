using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.DesktopViews.Items;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views;

namespace Sufni.App.Tests.Views.Items;

public class SessionMediaDesktopViewTests
{
    [AvaloniaFact]
    public async Task SessionMediaDesktopView_HidesMap_WhenSessionTrackIsEmpty()
    {
        var workspace = CreateWorkspace([]);

        await using var mounted = await MountAsync(workspace);

        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().Single();
        Assert.False(mapView.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_ShowsMap_WhenSessionTrackHasPoints()
    {
        var workspace = CreateWorkspace(
        [
            new TrackPoint(1, 2, 3, 4),
        ]);

        await using var mounted = await MountAsync(workspace);

        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().Single();
        Assert.True(mapView.IsVisible);
    }

    private static async Task<MountedSessionMediaDesktopView> MountAsync(SessionMediaWorkspaceStub workspace)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var view = new SessionMediaDesktopView
        {
            DataContext = workspace,
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedSessionMediaDesktopView(host, view);
    }

    private static SessionMediaWorkspaceStub CreateWorkspace(IReadOnlyList<TrackPoint> trackPoints)
    {
        var tileLayerService = Substitute.For<ITileLayerService>();
        tileLayerService.AvailableLayers.Returns(new ObservableCollection<TileLayerConfig>());

        var dialogService = Substitute.For<IDialogService>();
        var mapViewModel = new MapViewModel(tileLayerService, dialogService)
        {
            SessionTrackPoints = trackPoints.ToList(),
            FullTrackPoints = [],
        };

        return new SessionMediaWorkspaceStub(mapViewModel);
    }

    private sealed class SessionMediaWorkspaceStub : ISessionMediaWorkspace
    {
        private readonly MapViewModel mapViewModel;

        public SessionMediaWorkspaceStub(MapViewModel mapViewModel)
        {
            this.mapViewModel = mapViewModel;
        }

        public MapViewModel? MapViewModel => mapViewModel;
        public bool HasSessionTrackPoints => mapViewModel.SessionTrackPoints?.Count > 0;
        public SessionTimelineLinkViewModel Timeline { get; } = new();
        public double? MapVideoWidth => 400;
        public string? VideoUrl => null;
    }
}

internal sealed class MountedSessionMediaDesktopView(Window host, SessionMediaDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public SessionMediaDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}