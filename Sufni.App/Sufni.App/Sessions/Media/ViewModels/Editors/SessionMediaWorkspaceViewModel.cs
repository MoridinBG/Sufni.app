using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Graph.ViewModels.Editors;
namespace Sufni.App.Sessions.Media.ViewModels.Editors;

internal sealed class SessionMediaWorkspaceViewModel : ObservableObject, ISessionMediaWorkspace
{
    private readonly RecordedSessionContext context;
    private INotifyCollectionChanged? mediaPanes;

    public SessionMediaWorkspaceViewModel(RecordedSessionContext context)
    {
        this.context = context;
        context.PropertyChanged += OnContextPropertyChanged;
        SubscribeToMediaPanes(context.ExtensionSlots);
    }

    public bool HasMediaContent =>
        MapState.ReservesLayout ||
        MediaPaneState.ReservesLayout ||
        ExtensionSlots.MediaPanes.Count > 0;

    public MapViewModel? MapViewModel => context.MapViewModel;

    public SurfacePresentationState MapState => context.MapState;

    public SurfacePresentationState MediaPaneState => context.MediaPaneState;

    public SessionTimelineLinkViewModel Timeline => context.Timeline;

    public RecordedSessionExtensionSlots ExtensionSlots => context.ExtensionSlots;

    public double? MediaColumnWidth => context.MediaColumnWidth;

    public string? MediaUrl => context.MediaUrl;

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(RecordedSessionContext.MapViewModel):
                OnPropertyChanged(nameof(MapViewModel));
                break;
            case nameof(RecordedSessionContext.MapState):
                OnPropertyChanged(nameof(MapState));
                OnPropertyChanged(nameof(HasMediaContent));
                break;
            case nameof(RecordedSessionContext.MediaPaneState):
                OnPropertyChanged(nameof(MediaPaneState));
                OnPropertyChanged(nameof(HasMediaContent));
                break;
            case nameof(RecordedSessionContext.MediaColumnWidth):
                OnPropertyChanged(nameof(MediaColumnWidth));
                break;
            case nameof(RecordedSessionContext.MediaUrl):
                OnPropertyChanged(nameof(MediaUrl));
                break;
            case nameof(RecordedSessionContext.ExtensionSlots):
                SubscribeToMediaPanes(context.ExtensionSlots);
                OnPropertyChanged(nameof(ExtensionSlots));
                OnPropertyChanged(nameof(HasMediaContent));
                break;
        }
    }

    private void SubscribeToMediaPanes(RecordedSessionExtensionSlots slots)
    {
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
}
