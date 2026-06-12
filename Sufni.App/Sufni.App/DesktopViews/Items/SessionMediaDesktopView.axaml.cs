using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Models;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.DesktopViews.Items;

public partial class SessionMediaDesktopView : UserControl
{
    private bool applyingLayoutPreferences;
    private ISessionMediaWorkspace? workspace;
    private INotifyCollectionChanged? subscribedMediaPanes;
    private INotifyPropertyChanged? subscribedWorkspaceNotifications;

    public static readonly StyledProperty<SessionPaneGroupPreferences?> LayoutPreferencesProperty =
        AvaloniaProperty.Register<SessionMediaDesktopView, SessionPaneGroupPreferences?>(
            nameof(LayoutPreferences));

    static SessionMediaDesktopView()
    {
        LayoutPreferencesProperty.Changed.AddClassHandler<SessionMediaDesktopView>((view, _) => view.UpdateMediaRows());
    }

    public SessionPaneGroupPreferences? LayoutPreferences
    {
        get => GetValue(LayoutPreferencesProperty);
        set => SetValue(LayoutPreferencesProperty, value);
    }

    public SessionMediaDesktopView()
    {
        InitializeComponent();
        SessionSectionGridSizing.AttachCommit(MediaSplitter, PublishLayoutPreferences);
        SessionSectionGridSizing.AttachCommit(MediaPaneSplitter, PublishLayoutPreferences);
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
        applyingLayoutPreferences = true;
        try
        {
            ApplyDefaultMediaRows();
            ApplyStoredMediaRows();
        }
        finally
        {
            applyingLayoutPreferences = false;
        }
    }

    private void ApplyDefaultMediaRows()
    {
        var hasMedia = workspace?.MediaPaneState.ReservesLayout == true;
        var hasMap = workspace?.MapState.ReservesLayout == true;
        var hasMediaPanes = workspace?.ExtensionSlots.MediaPanes.Count > 0;
        var showSplitter = hasMap && hasMediaPanes;

        MediaContentRoot.RowDefinitions[0].Height = hasMedia ? GridLength.Auto : new GridLength(0);
        MediaContentRoot.RowDefinitions[2].Height = hasMap ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        MediaContentRoot.RowDefinitions[3].Height = showSplitter ? GridLength.Auto : new GridLength(0);
        MediaContentRoot.RowDefinitions[4].Height = hasMediaPanes ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        MediaPaneSplitter.IsVisible = showSplitter;
        MediaPanesHost.IsVisible = hasMediaPanes;
    }

    private void ApplyStoredMediaRows()
    {
        var paneRows = GetVisiblePaneRows();
        if (paneRows.Count < 2)
        {
            return;
        }

        SessionSectionGridSizing.TryApplyRowRatios(MediaContentRoot, LayoutPreferences, paneRows);
    }

    private IReadOnlyList<(int Row, string PaneId)> GetVisiblePaneRows()
    {
        var rows = new List<(int Row, string PaneId)>();
        if (workspace?.MediaPaneState.ReservesLayout == true)
        {
            rows.Add((0, SessionLayoutPaneIds.Media));
        }

        if (workspace?.MapState.ReservesLayout == true)
        {
            rows.Add((2, SessionLayoutPaneIds.Map));
        }

        if (workspace?.ExtensionSlots.MediaPanes.Count > 0)
        {
            rows.Add((4, SessionLayoutPaneIds.ExtensionMedia));
        }

        return rows;
    }

    private void PublishLayoutPreferences()
    {
        if (applyingLayoutPreferences || VisualRoot is null)
        {
            return;
        }

        var paneRows = GetVisiblePaneRows();
        if (paneRows.Count < 2)
        {
            LayoutPreferences = null;
            return;
        }

        var panes = paneRows
            .Select(row => (row.PaneId, MediaContentRoot.Children
                .OfType<Control>()
                .Where(control => Grid.GetRow(control) == row.Row)
                .Select(control => control.Bounds.Height)
                .DefaultIfEmpty(0)
                .Max()))
            .ToArray();
        LayoutPreferences = SessionSectionGridSizing.CaptureRatios(panes);
    }
}
