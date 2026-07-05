using System;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
namespace Sufni.App.Sessions.Media.ViewModels.Editors;

internal sealed class SessionMediaWorkspaceViewModel : ObservableObject, ISessionMediaWorkspace, IDisposable
{
    private readonly Func<MapViewModel?> mapViewModel;
    private readonly Func<RecordedSessionExtensionSlots> extensionSlots;
    private readonly IDisposable stateSubscription;
    private MapViewModel? currentMapViewModel;
    private RecordedSessionExtensionSlots? currentExtensionSlots;
    private INotifyCollectionChanged? mediaPanes;
    private SurfacePresentationState mapState = SurfacePresentationState.Hidden;
    private SurfacePresentationState mediaPaneState = SurfacePresentationState.Hidden;
    private double? mediaColumnWidth;
    private string? mediaUrl;

    public SessionMediaWorkspaceViewModel(
        IObservable<RecordedSessionEditorState> state,
        Func<MapViewModel?> mapViewModel,
        SessionTimelineLinkViewModel timeline,
        Func<RecordedSessionExtensionSlots> extensionSlots)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(mapViewModel);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(extensionSlots);

        this.mapViewModel = mapViewModel;
        Timeline = timeline;
        this.extensionSlots = extensionSlots;
        stateSubscription = state.Subscribe(ApplyState);
    }

    public bool HasMediaContent =>
        MapState.ReservesLayout ||
        MediaPaneState.ReservesLayout ||
        ExtensionSlots.MediaPanes.Count > 0;

    public MapViewModel? MapViewModel => currentMapViewModel;

    public SurfacePresentationState MapState => mapState;

    public SurfacePresentationState MediaPaneState => mediaPaneState;

    public SessionTimelineLinkViewModel Timeline { get; }

    public RecordedSessionExtensionSlots ExtensionSlots => currentExtensionSlots ?? extensionSlots();

    public double? MediaColumnWidth => mediaColumnWidth;

    public string? MediaUrl => mediaUrl;

    private void ApplyState(RecordedSessionEditorState state)
    {
        var nextMapViewModel = mapViewModel();
        if (!ReferenceEquals(currentMapViewModel, nextMapViewModel))
        {
            currentMapViewModel = nextMapViewModel;
            OnPropertyChanged(nameof(MapViewModel));
        }

        var nextExtensionSlots = extensionSlots();
        if (!ReferenceEquals(currentExtensionSlots, nextExtensionSlots))
        {
            currentExtensionSlots = nextExtensionSlots;
            SubscribeToMediaPanes(nextExtensionSlots);
            OnPropertyChanged(nameof(ExtensionSlots));
            OnPropertyChanged(nameof(HasMediaContent));
        }

        if (SetProperty(ref mapState, state.Presentation.MapState, nameof(MapState)))
        {
            OnPropertyChanged(nameof(HasMediaContent));
        }

        if (SetProperty(ref mediaPaneState, state.Presentation.MediaPaneState, nameof(MediaPaneState)))
        {
            OnPropertyChanged(nameof(HasMediaContent));
        }

        SetProperty(ref mediaColumnWidth, state.Presentation.MediaColumnWidth, nameof(MediaColumnWidth));
        SetProperty(ref mediaUrl, state.Presentation.MediaUrl, nameof(MediaUrl));
    }

    private void SubscribeToMediaPanes(RecordedSessionExtensionSlots slots)
    {
        if (ReferenceEquals(mediaPanes, slots.MediaPanes))
        {
            return;
        }

        if (mediaPanes is not null)
        {
            mediaPanes.CollectionChanged -= OnMediaPanesChanged;
        }

        mediaPanes = slots.MediaPanes;
        mediaPanes.CollectionChanged += OnMediaPanesChanged;
    }

    private void OnMediaPanesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(HasMediaContent));
    }

    public void Dispose()
    {
        stateSubscription.Dispose();
        if (mediaPanes is not null)
        {
            mediaPanes.CollectionChanged -= OnMediaPanesChanged;
            mediaPanes = null;
        }
    }
}
