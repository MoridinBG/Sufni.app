using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Graph.ViewModels.Editors;
namespace Sufni.App.LiveDaq.ViewModels.Editors;

public sealed class LiveSessionMediaWorkspaceViewModel : ObservableObject, ISessionMediaWorkspace, IDisposable
{
    private bool isInitialized;
    private bool mapExpected;

    public bool HasMediaContent => MapState.ReservesLayout || MediaPaneState.ReservesLayout;
    public MapViewModel? MapViewModel { get; }
    public SurfacePresentationState MapState => !mapExpected
        ? SurfacePresentationState.Hidden
        : MapViewModel?.SessionTrackPoints?.Count > 0
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.WaitingForData("Waiting for map data.");
    public SurfacePresentationState MediaPaneState => SurfacePresentationState.Hidden;
    public SessionTimelineLinkViewModel Timeline { get; }
    public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();
    public double? MediaColumnWidth { get; } = 400;
    public string? MediaUrl => null;

    public LiveSessionMediaWorkspaceViewModel()
    {
        Timeline = new SessionTimelineLinkViewModel();
    }

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

    public void SetTrackPoints(IReadOnlyList<TrackPoint>? trackPoints, TrackTimeRange? timelineContext)
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

    public void Dispose() => MapViewModel?.Dispose();
}
