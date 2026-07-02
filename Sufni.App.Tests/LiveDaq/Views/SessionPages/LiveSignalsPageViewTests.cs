using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;

using Sufni.App.LiveDaq.ViewModels.SessionPages;
using Sufni.App.LiveDaq.Views.SessionPages;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Views.Controls;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Views;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Extensions;

namespace Sufni.App.Tests.LiveDaq.Views.SessionPages;

[Collection("Ui")]
public class LiveSignalsPageViewTests
{
    [AvaloniaFact]
    public async Task LiveSignalsPageView_BindsPlaceholderContainers_ToWorkspaceState()
    {
        var workspace = Substitute.For<ILiveSessionSignalsWorkspace>();
        workspace.SignalBatches.Returns(new Subject<LiveSignalBatch>());
        workspace.PlotRanges.Returns(new LiveSessionPlotRanges(TravelMaximum: 180, VelocityMaximum: 5, ImuMaximum: 5));
        workspace.Timeline.Returns(new SessionTimelineLinkViewModel());
        workspace.TravelSignalState.Returns(SurfacePresentationState.Ready);
        workspace.VelocitySignalState.Returns(SurfacePresentationState.WaitingForData("Waiting for live velocity data."));
        workspace.ImuSignalState.Returns(SurfacePresentationState.Hidden);
        workspace.PitchRollSignalState.Returns(SurfacePresentationState.WaitingForData("Waiting for live pitch/roll data."));
        workspace.SpeedSignalState.Returns(SurfacePresentationState.WaitingForData("Waiting for live speed data."));
        workspace.ElevationSignalState.Returns(SurfacePresentationState.Hidden);
        workspace.TrackPoints.Returns(
        [
            new TrackPoint(0, 0, 0, 100, 5),
            new TrackPoint(1, 100, 100, 101, 6),
        ]);
        workspace.TrackTimelineContext.Returns(new TrackTimeRange(0, 1));

        var page = new LiveSignalsPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var pageScrollViewer = mounted.View.FindControl<ScrollViewer>("PageScrollViewer");
        var rowsView = mounted.View.GetVisualDescendants()
            .OfType<LiveSignalRowsView>()
            .SingleOrDefault();

        Assert.NotNull(pageScrollViewer);
        Assert.NotNull(rowsView);
        Assert.Same(workspace, rowsView!.DataContext);
        Assert.True(rowsView.HideRightAxis);
    }

    [AvaloniaFact]
    public async Task LiveSignalsPageView_RendersMapBelowSignals_WhenMapReady()
    {
        var signalsWorkspace = Substitute.For<ILiveSessionSignalsWorkspace>();
        signalsWorkspace.SignalBatches.Returns(new Subject<LiveSignalBatch>());
        signalsWorkspace.PlotRanges.Returns(new LiveSessionPlotRanges(TravelMaximum: 180, VelocityMaximum: 5, ImuMaximum: 5));
        signalsWorkspace.Timeline.Returns(new SessionTimelineLinkViewModel());
        signalsWorkspace.TravelSignalState.Returns(SurfacePresentationState.Ready);
        signalsWorkspace.VelocitySignalState.Returns(SurfacePresentationState.Ready);
        signalsWorkspace.ImuSignalState.Returns(SurfacePresentationState.Hidden);
        signalsWorkspace.PitchRollSignalState.Returns(SurfacePresentationState.Hidden);
        signalsWorkspace.SpeedSignalState.Returns(SurfacePresentationState.Hidden);
        signalsWorkspace.ElevationSignalState.Returns(SurfacePresentationState.Hidden);
        var mediaWorkspace = CreateMediaWorkspace(
        [
            new TrackPoint(0, 0, 0, null),
            new TrackPoint(1, 100, 100, null),
        ]);

        var page = new LiveSignalsPageViewModel(signalsWorkspace, mediaWorkspace);

        await using var mounted = await MountAsync(page);

        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().Single();

        Assert.NotNull(mapHost);
        Assert.True(mapHost!.IsVisible);
        Assert.True(mapView.IsVisible);
        Assert.Same(mediaWorkspace.MapViewModel, mapView.DataContext);
        Assert.Same(mediaWorkspace.Timeline, mapView.Timeline);
        Assert.NotNull(mapView.FindControl<ComboBox>("TileProviderComboBox"));
    }

    private static async Task<MountedLiveSignalsPageView> MountAsync(LiveSignalsPageViewModel page)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new LiveSignalsPageView
        {
            DataContext = page,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedLiveSignalsPageView(host, view);
    }

    private static SessionMediaWorkspaceStub CreateMediaWorkspace(IReadOnlyList<TrackPoint> trackPoints)
    {
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
        tileLayerService.AvailableLayers.Returns(new ObservableCollection<TileLayerConfig>());

        var mapViewModel = new MapViewModel(tileLayerService, Substitute.For<IDialogService>(), new InlineUiThreadDispatcher())
        {
            FullTrackPoints = [],
            SessionTrackPoints = trackPoints.ToList(),
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

        public bool HasMediaContent => MapState.ReservesLayout;
        public MapViewModel? MapViewModel => mapViewModel;
        public SurfacePresentationState MapState => mapViewModel.SessionTrackPoints?.Count > 0
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState MediaPaneState => SurfacePresentationState.Hidden;
        public SessionTimelineLinkViewModel Timeline { get; } = new();
        public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();
        public double? MediaColumnWidth => 400;
        public string? MediaUrl => null;
    }
}

internal sealed record MountedLiveSignalsPageView(Window Host, LiveSignalsPageView View) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
