using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.DesktopViews.Items;

public partial class SessionMediaDesktopView : UserControl
{
    private ISessionMediaWorkspace? workspace;
    private INotifyCollectionChanged? subscribedMediaPanes;
    private INotifyPropertyChanged? subscribedWorkspaceNotifications;

    public SessionMediaDesktopView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SetWorkspace(DataContext as ISessionMediaWorkspace);
        SetWorkspace(DataContext as ISessionMediaWorkspace);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetWorkspace(DataContext as ISessionMediaWorkspace);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetWorkspace(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SetWorkspace(ISessionMediaWorkspace? value)
    {
        if (ReferenceEquals(workspace, value))
        {
            return;
        }

        if (subscribedMediaPanes is not null)
        {
            subscribedMediaPanes.CollectionChanged -= OnMediaPanesChanged;
            subscribedMediaPanes = null;
        }

        if (subscribedWorkspaceNotifications is not null)
        {
            subscribedWorkspaceNotifications.PropertyChanged -= OnWorkspacePropertyChanged;
            subscribedWorkspaceNotifications = null;
        }

        workspace = value;

        if (workspace is not null)
        {
            subscribedMediaPanes = workspace.ExtensionSlots.MediaPanes;
            subscribedMediaPanes.CollectionChanged += OnMediaPanesChanged;

            subscribedWorkspaceNotifications = workspace as INotifyPropertyChanged;
            if (subscribedWorkspaceNotifications is not null)
            {
                subscribedWorkspaceNotifications.PropertyChanged += OnWorkspacePropertyChanged;
            }
        }

        UpdateMediaRows();
    }

    private void OnMediaPanesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        UpdateMediaRows();
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ISessionMediaWorkspace.MapState) or nameof(ISessionMediaWorkspace.HasMediaContent))
        {
            UpdateMediaRows();
        }
    }

    private void UpdateMediaRows()
    {
        var hasMap = workspace?.MapState.ReservesLayout == true;
        var hasMediaPanes = workspace?.ExtensionSlots.MediaPanes.Count > 0;
        var showSplitter = hasMap && hasMediaPanes;

        MediaContentRoot.RowDefinitions[2].Height = hasMap ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        MediaContentRoot.RowDefinitions[3].Height = showSplitter ? GridLength.Auto : new GridLength(0);
        MediaContentRoot.RowDefinitions[4].Height = hasMediaPanes ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        MediaPaneSplitter.IsVisible = showSplitter;
        MediaPanesHost.IsVisible = hasMediaPanes;
    }
}
