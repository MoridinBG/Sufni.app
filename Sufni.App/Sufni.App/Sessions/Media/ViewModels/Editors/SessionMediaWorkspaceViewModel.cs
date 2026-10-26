using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Video.ViewModels;
namespace Sufni.App.Sessions.Media.ViewModels.Editors;

internal sealed class SessionMediaWorkspaceViewModel : ObservableObject, ISessionMediaWorkspace, IDisposable
{
    private readonly Func<MapViewModel?> mapViewModel;
    private readonly Func<RecordedSessionExtensionSlots> extensionSlots;
    private readonly VideoPaneViewModel? videoPane;
    private readonly IDisposable stateSubscription;
    private MapViewModel? currentMapViewModel;
    private RecordedSessionExtensionSlots? currentExtensionSlots;
    private SurfacePresentationState mapState = SurfacePresentationState.Hidden;
    private double? mediaColumnWidth;

    public SessionMediaWorkspaceViewModel(
        IObservable<RecordedSessionEditorState> state,
        Func<MapViewModel?> mapViewModel,
        SessionTimelineLinkViewModel timeline,
        Func<RecordedSessionExtensionSlots> extensionSlots,
        VideoPaneViewModel? videoPane)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(mapViewModel);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(extensionSlots);

        this.mapViewModel = mapViewModel;
        Timeline = timeline;
        this.extensionSlots = extensionSlots;
        this.videoPane = videoPane;
        if (videoPane is not null)
        {
            videoPane.PropertyChanged += OnVideoPanePropertyChanged;
        }

        stateSubscription = state.Subscribe(ApplyState);
    }

    public bool HasMediaContent => MapState.ReservesLayout || Video is not null;

    public MapViewModel? MapViewModel => currentMapViewModel;

    public SurfacePresentationState MapState => mapState;

    public SessionTimelineLinkViewModel Timeline { get; }

    public RecordedSessionExtensionSlots ExtensionSlots => currentExtensionSlots ?? extensionSlots();

    public double? MediaColumnWidth => mediaColumnWidth;

    public VideoPaneViewModel? Video => videoPane is { HasVideo: true } ? videoPane : null;

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
            OnPropertyChanged(nameof(ExtensionSlots));
        }

        if (SetProperty(ref mapState, state.Presentation.MapState, nameof(MapState)))
        {
            OnPropertyChanged(nameof(HasMediaContent));
        }

        SetProperty(ref mediaColumnWidth, state.Presentation.MediaColumnWidth, nameof(MediaColumnWidth));
    }

    private void OnVideoPanePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(VideoPaneViewModel.HasVideo))
        {
            OnPropertyChanged(nameof(Video));
            OnPropertyChanged(nameof(HasMediaContent));
        }
    }

    public void Dispose()
    {
        stateSubscription.Dispose();
        if (videoPane is not null)
        {
            videoPane.PropertyChanged -= OnVideoPanePropertyChanged;
        }
    }
}
