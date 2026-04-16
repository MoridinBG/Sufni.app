using System.ComponentModel;
using Avalonia.Controls;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.DesktopViews.Items;

public partial class SessionMediaDesktopView : UserControl
{
    private INotifyPropertyChanged? workspaceNotifications;
    private INotifyPropertyChanged? mapViewModelNotifications;

    public SessionMediaDesktopView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachWorkspaceBindings();
    }

    private void AttachWorkspaceBindings()
    {
        if (workspaceNotifications is not null)
        {
            workspaceNotifications.PropertyChanged -= OnWorkspacePropertyChanged;
            workspaceNotifications = null;
        }

        if (mapViewModelNotifications is not null)
        {
            mapViewModelNotifications.PropertyChanged -= OnMapViewModelPropertyChanged;
            mapViewModelNotifications = null;
        }

        if (DataContext is INotifyPropertyChanged workspaceNotifier)
        {
            workspaceNotifications = workspaceNotifier;
            workspaceNotifications.PropertyChanged += OnWorkspacePropertyChanged;
        }

        if ((DataContext as ISessionMediaWorkspace)?.MapViewModel is INotifyPropertyChanged mapViewModelNotifier)
        {
            mapViewModelNotifications = mapViewModelNotifier;
            mapViewModelNotifications.PropertyChanged += OnMapViewModelPropertyChanged;
        }

        UpdateMapViewBindings();
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISessionMediaWorkspace.HasSessionTrackPoints)
            || e.PropertyName == nameof(ISessionMediaWorkspace.MapViewModel))
        {
            AttachWorkspaceBindings();
        }
    }

    private void OnMapViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MapViewModel.HasSessionTrackPoints))
        {
            UpdateMapViewBindings();
        }
    }

    private void UpdateMapViewBindings()
    {
        if (DataContext is not ISessionMediaWorkspace workspace)
        {
            MapViewControl.Timeline = null;
            MapViewControl.IsVisible = false;
            return;
        }

        MapViewControl.Timeline = workspace.Timeline;
        MapViewControl.IsVisible = workspace.HasSessionTrackPoints;
    }
}