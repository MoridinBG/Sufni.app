using Sufni.App.Sessions.Presentation;
using System.ComponentModel;
using Sufni.App.MapsAndTracks.Views;

namespace Sufni.App.Sessions.Media.Views;

// Keeps a MapView's DataContext, matched track, and timeline in sync with a
// session media workspace, following the same subscribe/sync/unsubscribe pattern
// used by SessionMediaDesktopView. Shared by the mobile signals page views so the
// map wiring lives in one place rather than being copied into each view.
internal sealed class MediaWorkspaceMapBinder
{
    private readonly MapView mapView;
    private ISessionMediaWorkspace? mediaWorkspace;
    private INotifyPropertyChanged? subscribedMediaWorkspace;

    public MediaWorkspaceMapBinder(MapView mapView)
    {
        this.mapView = mapView;
    }

    public void SetWorkspace(ISessionMediaWorkspace? value)
    {
        if (ReferenceEquals(mediaWorkspace, value))
        {
            return;
        }

        if (subscribedMediaWorkspace is not null)
        {
            subscribedMediaWorkspace.PropertyChanged -= OnMediaWorkspacePropertyChanged;
            subscribedMediaWorkspace = null;
        }

        mediaWorkspace = value;

        subscribedMediaWorkspace = mediaWorkspace as INotifyPropertyChanged;
        if (subscribedMediaWorkspace is not null)
        {
            subscribedMediaWorkspace.PropertyChanged += OnMediaWorkspacePropertyChanged;
        }

        SyncMapView();
    }

    private void OnMediaWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ISessionMediaWorkspace.MapViewModel)
            or nameof(ISessionMediaWorkspace.MatchedTrack))
        {
            SyncMapView();
        }
    }

    private void SyncMapView()
    {
        mapView.DataContext = mediaWorkspace?.MapViewModel;
        mapView.MatchedTrack = mediaWorkspace?.MatchedTrack;
        mapView.Timeline = mediaWorkspace?.Timeline;
    }
}
