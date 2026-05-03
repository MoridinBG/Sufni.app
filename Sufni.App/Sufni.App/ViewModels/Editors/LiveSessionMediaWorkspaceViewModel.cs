using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.ViewModels;

namespace Sufni.App.ViewModels.Editors;

public sealed class LiveSessionMediaWorkspaceViewModel : ViewModelBase, ISessionMediaWorkspace
{
    private bool isInitialized;
    private bool mapExpected;

    public bool HasMediaContent => MapState.ReservesLayout || VideoState.ReservesLayout;
    public MapViewModel? MapViewModel { get; }
    public SurfacePresentationState MapState => !mapExpected
        ? SurfacePresentationState.Hidden
        : MapViewModel?.SessionTrackPoints?.Count > 0
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.WaitingForData("Waiting for map data.");
    public SurfacePresentationState VideoState => SurfacePresentationState.Hidden;
    public SessionTimelineLinkViewModel Timeline { get; }
    public double? MapVideoWidth { get; } = 400;
    public string? VideoUrl => null;

    public LiveSessionMediaWorkspaceViewModel()
        : this(tileLayerService: null, dialogService: null, new SessionTimelineLinkViewModel())
    {
    }

    public LiveSessionMediaWorkspaceViewModel(
        ITileLayerService? tileLayerService,
        IDialogService? dialogService,
        SessionTimelineLinkViewModel timeline)
    {
        Timeline = timeline;

        if (tileLayerService is null || dialogService is null)
        {
            return;
        }

        MapViewModel = new MapViewModel(tileLayerService, dialogService)
        {
            FullTrackPoints = [],
            SessionTrackPoints = [],
        };
    }

    public async Task InitializeAsync()
    {
        if (MapViewModel is null || isInitialized)
        {
            return;
        }

        isInitialized = true;
        await MapViewModel.InitializeAsync();
    }

    public void ApplySessionHeader(LiveSessionHeader? sessionHeader)
    {
        mapExpected = sessionHeader is { AcceptedGpsFixHz: > 0 };
        OnPropertyChanged(nameof(MapState));
        OnPropertyChanged(nameof(HasMediaContent));
    }

    public void SetTrackPoints(IReadOnlyList<TrackPoint>? trackPoints, TrackTimelineContext? timelineContext)
    {
        if (MapViewModel is null)
        {
            return;
        }

        MapViewModel.SessionTrackPoints = trackPoints?.ToList() ?? [];
        MapViewModel.TimelineContext = timelineContext;
        OnPropertyChanged(nameof(MapState));
        OnPropertyChanged(nameof(HasMediaContent));
    }
}
