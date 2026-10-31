using Sufni.App.MapsAndTracks.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Shared.Common;

using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Video.ViewModels;
namespace Sufni.App.LiveDaq.ViewModels.Editors;

public sealed class LiveSessionMediaWorkspaceViewModel : ObservableObject, ISessionMediaWorkspace, IDisposable
{
    private bool isInitialized;
    private bool mapExpected;

    public bool HasMediaContent => MapState.ReservesLayout;
    public MapViewModel MapViewModel { get; }
    public SurfacePresentationState MapState => !mapExpected
        ? SurfacePresentationState.Hidden
        : MapViewModel.SessionTrackPoints?.Count > 0
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.WaitingForData("Waiting for map data.");
    public SessionTimelineLinkViewModel Timeline { get; }
    public MatchedTrackOverlay? MatchedTrack => null;
    public VideoPaneViewModel? Video => null;

    public LiveSessionMediaWorkspaceViewModel(
        IMapViewModelFactory mapViewModelFactory,
        SessionTimelineLinkViewModel timeline)
    {
        Timeline = timeline;
        var mapViewModel = mapViewModelFactory.Create();
        mapViewModel.FullTrackPoints = [];
        mapViewModel.SessionTrackPoints = [];
        MapViewModel = mapViewModel;
    }

    public async Task InitializeAsync()
    {
        if (isInitialized)
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

    public void SetTrackPoints(IReadOnlyList<TrackPoint>? trackPoints, TrackTimeRange? timelineContext)
    {
        MapViewModel.SessionTrackPoints = trackPoints?.ToList() ?? [];
        MapViewModel.TimelineContext = timelineContext;
        OnPropertyChanged(nameof(MapState));
        OnPropertyChanged(nameof(HasMediaContent));
    }

    public void Dispose() => MapViewModel.Dispose();
}
