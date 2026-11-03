using Sufni.App.Sessions.Detail.DesktopViews.Controls;
using Sufni.App.Sessions.Models;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Shared.DesktopViews.Controls;
namespace Sufni.App.Sessions.Media.DesktopViews.Items;

public partial class SessionMediaDesktopView : UserControl
{
    private static readonly string[] PaneIds = [SessionLayoutPaneIds.Map, SessionLayoutPaneIds.Video];

    private bool applyingLayoutPreferences;
    private ISessionMediaWorkspace? workspace;
    private INotifyPropertyChanged? subscribedWorkspaceNotifications;

    public static readonly StyledProperty<SessionPaneGroupPreferences?> LayoutPreferencesProperty =
        AvaloniaProperty.Register<SessionMediaDesktopView, SessionPaneGroupPreferences?>(
            nameof(LayoutPreferences));

    static SessionMediaDesktopView()
    {
        LayoutPreferencesProperty.Changed.AddClassHandler<SessionMediaDesktopView>((view, _) => view.UpdateMediaLayout());
    }

    public SessionPaneGroupPreferences? LayoutPreferences
    {
        get => GetValue(LayoutPreferencesProperty);
        set => SetValue(LayoutPreferencesProperty, value);
    }

    public SessionMediaDesktopView()
    {
        InitializeComponent();

        MediaSplit.FirstPaneId = SessionLayoutPaneIds.Map;
        MediaSplit.SecondPaneId = SessionLayoutPaneIds.Video;
        MediaSplit.PropertyChanged += OnMediaSplitPropertyChanged;

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

        if (subscribedWorkspaceNotifications is not null)
        {
            subscribedWorkspaceNotifications.PropertyChanged -= OnWorkspacePropertyChanged;
            subscribedWorkspaceNotifications = null;
        }

        workspace = value;

        subscribedWorkspaceNotifications = workspace as INotifyPropertyChanged;
        if (subscribedWorkspaceNotifications is not null)
        {
            subscribedWorkspaceNotifications.PropertyChanged += OnWorkspacePropertyChanged;
        }

        SyncMapView();
        UpdateMediaLayout();
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ISessionMediaWorkspace.MapState)
            or nameof(ISessionMediaWorkspace.MapViewModel)
            or nameof(ISessionMediaWorkspace.MatchedTrack)
            or nameof(ISessionMediaWorkspace.Video))
        {
            SyncMapView();
            UpdateMediaLayout();
        }
    }

    private void SyncMapView()
    {
        MapViewControl.DataContext = workspace?.MapViewModel;
        MapViewControl.MatchedTrack = workspace?.MatchedTrack;
        MapViewControl.Timeline = workspace?.Timeline;
    }

    private void OnMediaSplitPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == CollapsibleSplitView.PreferencesProperty)
        {
            PublishLayoutPreferences();
        }
    }

    private void UpdateMediaLayout()
    {
        applyingLayoutPreferences = true;
        try
        {
            var hasMap = HasMapPane();
            var hasVideo = HasVideoPane();

            MediaSplit.HasFirstPane = hasMap;
            MediaSplit.HasSecondPane = hasVideo;
            MediaSplit.CanCollapseFirstPane = hasMap;
            MediaSplit.CanCollapseSecondPane = hasVideo;
            MediaSplit.Preferences =
                hasMap && hasVideo &&
                LayoutPreferences is { } preferences &&
                preferences.TryGetPaneStates(PaneIds, out var states)
                    ? new SessionPaneGroupPreferences(
                        states.Select(state => new SessionPaneSizePreference(state.PaneId, state.Ratio, state.IsCollapsed)).ToArray())
                    : null;
        }
        finally
        {
            applyingLayoutPreferences = false;
        }
    }

    private void PublishLayoutPreferences()
    {
        if (applyingLayoutPreferences || VisualRoot is null)
        {
            return;
        }

        LayoutPreferences = HasMapPane() && HasVideoPane()
            ? MediaSplit.CaptureCurrentPreferences()
            : null;
    }

    private bool HasMapPane() => workspace?.MapState.ReservesLayout == true;

    private bool HasVideoPane() => workspace?.Video is not null;
}
