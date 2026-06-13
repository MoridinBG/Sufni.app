using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.DesktopViews.Controls;
using Sufni.App.Models;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.DesktopViews.Items;

public partial class SessionMediaDesktopView : UserControl
{
    private const string LowerMediaGroupPaneId = "__session_media_lower_group";

    private bool applyingLayoutPreferences;
    private ISessionMediaWorkspace? workspace;
    private INotifyCollectionChanged? subscribedMediaPanes;
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

        PrimaryMediaSplit.FirstPaneId = SessionLayoutPaneIds.Media;
        PrimaryMediaSplit.SecondPaneId = LowerMediaGroupPaneId;
        LowerMediaSplit.FirstPaneId = SessionLayoutPaneIds.Map;
        LowerMediaSplit.SecondPaneId = SessionLayoutPaneIds.ExtensionMedia;
        PrimaryMediaSplit.PropertyChanged += OnPrimaryMediaSplitPropertyChanged;
        LowerMediaSplit.PropertyChanged += OnLowerMediaSplitPropertyChanged;

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

        UpdateMediaLayout();
    }

    private void OnMediaPanesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        UpdateMediaLayout();
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ISessionMediaWorkspace.MapState)
            or nameof(ISessionMediaWorkspace.MediaPaneState)
            or nameof(ISessionMediaWorkspace.HasMediaContent))
        {
            UpdateMediaLayout();
        }
    }

    private void OnPrimaryMediaSplitPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == CollapsibleSplitView.PreferencesProperty)
        {
            PublishLayoutPreferences();
        }
    }

    private void OnLowerMediaSplitPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
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
            var hasMedia = HasMediaPane();
            var hasMap = HasMapPane();
            var hasExtensionMedia = HasExtensionMediaPane();

            PrimaryMediaSplit.HasFirstPane = hasMedia;
            PrimaryMediaSplit.HasSecondPane = hasMap || hasExtensionMedia;
            PrimaryMediaSplit.CanCollapseFirstPane = hasMedia;
            PrimaryMediaSplit.CanCollapseSecondPane = hasMedia && (hasMap != hasExtensionMedia);
            LowerMediaSplit.HasFirstPane = hasMap;
            LowerMediaSplit.HasSecondPane = hasExtensionMedia;
            LowerMediaSplit.CanCollapseFirstPane = hasMap;
            LowerMediaSplit.CanCollapseSecondPane = hasExtensionMedia;
            MediaPanesHost.IsVisible = hasExtensionMedia;

            ApplyStoredMediaLayout();
        }
        finally
        {
            applyingLayoutPreferences = false;
        }
    }

    private void ApplyStoredMediaLayout()
    {
        var visiblePaneIds = GetVisiblePaneIds();
        if (visiblePaneIds.Count < 2 ||
            LayoutPreferences is not { } preferences ||
            !preferences.TryGetPaneStates(visiblePaneIds, out var states))
        {
            PrimaryMediaSplit.Preferences = null;
            LowerMediaSplit.Preferences = null;
            return;
        }

        if (visiblePaneIds.Count == 2)
        {
            ApplyTwoPanePreferences(states);
            return;
        }

        ApplyThreePanePreferences(states);
    }

    private void ApplyTwoPanePreferences(IReadOnlyList<SessionPaneStatePreference> states)
    {
        if (HasMediaPane())
        {
            var media = states.Single(state => state.PaneId == SessionLayoutPaneIds.Media);
            var lower = states.Single(state => state.PaneId != SessionLayoutPaneIds.Media);

            PrimaryMediaSplit.Preferences = new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Media, media.Ratio, media.IsCollapsed),
                new SessionPaneSizePreference(LowerMediaGroupPaneId, lower.Ratio, lower.IsCollapsed),
            ]);
            LowerMediaSplit.Preferences = null;
            return;
        }

        PrimaryMediaSplit.Preferences = null;
        LowerMediaSplit.Preferences = new SessionPaneGroupPreferences(
        [
            ToSizePreference(states.Single(state => state.PaneId == SessionLayoutPaneIds.Map)),
            ToSizePreference(states.Single(state => state.PaneId == SessionLayoutPaneIds.ExtensionMedia)),
        ]);
    }

    private void ApplyThreePanePreferences(IReadOnlyList<SessionPaneStatePreference> states)
    {
        var media = states.Single(state => state.PaneId == SessionLayoutPaneIds.Media);
        var map = states.Single(state => state.PaneId == SessionLayoutPaneIds.Map);
        var extensionMedia = states.Single(state => state.PaneId == SessionLayoutPaneIds.ExtensionMedia);
        var lowerTotal = map.Ratio + extensionMedia.Ratio;
        if (!double.IsFinite(lowerTotal) || lowerTotal <= 0)
        {
            PrimaryMediaSplit.Preferences = null;
            LowerMediaSplit.Preferences = null;
            return;
        }

        PrimaryMediaSplit.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference(SessionLayoutPaneIds.Media, media.Ratio, media.IsCollapsed),
            new SessionPaneSizePreference(LowerMediaGroupPaneId, lowerTotal),
        ]);
        LowerMediaSplit.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference(SessionLayoutPaneIds.Map, map.Ratio / lowerTotal, map.IsCollapsed),
            new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, extensionMedia.Ratio / lowerTotal, extensionMedia.IsCollapsed),
        ]);
    }

    private void PublishLayoutPreferences()
    {
        if (applyingLayoutPreferences || VisualRoot is null)
        {
            return;
        }

        var visiblePaneIds = GetVisiblePaneIds();
        if (visiblePaneIds.Count < 2)
        {
            LayoutPreferences = null;
            return;
        }

        LayoutPreferences = BuildLayoutPreferencesFromSplits(visiblePaneIds);
    }

    private SessionPaneGroupPreferences? BuildLayoutPreferencesFromSplits(IReadOnlyList<string> visiblePaneIds)
    {
        if (visiblePaneIds.Count == 2)
        {
            return HasMediaPane()
                ? BuildMediaAndLowerPanePreferences()
                : LowerMediaSplit.CaptureCurrentPreferences();
        }

        return BuildThreePanePreferences();
    }

    private SessionPaneGroupPreferences? BuildMediaAndLowerPanePreferences()
    {
        var primaryPreferences = PrimaryMediaSplit.CaptureCurrentPreferences();
        if (primaryPreferences is null ||
            !primaryPreferences.TryGetPaneStates([SessionLayoutPaneIds.Media, LowerMediaGroupPaneId], out var states))
        {
            return null;
        }

        var lowerPaneId = HasMapPane()
            ? SessionLayoutPaneIds.Map
            : SessionLayoutPaneIds.ExtensionMedia;
        return new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference(
                SessionLayoutPaneIds.Media,
                states[0].Ratio,
                states[0].IsCollapsed),
            new SessionPaneSizePreference(
                lowerPaneId,
                states[1].Ratio,
                states[1].IsCollapsed),
        ]);
    }

    private SessionPaneGroupPreferences? BuildThreePanePreferences()
    {
        var primaryPreferences = PrimaryMediaSplit.CaptureCurrentPreferences();
        var lowerPreferences = LowerMediaSplit.CaptureCurrentPreferences();
        if (primaryPreferences is null ||
            lowerPreferences is null ||
            !primaryPreferences.TryGetPaneStates([SessionLayoutPaneIds.Media, LowerMediaGroupPaneId], out var primaryStates) ||
            !lowerPreferences.TryGetPaneStates([SessionLayoutPaneIds.Map, SessionLayoutPaneIds.ExtensionMedia], out var lowerStates))
        {
            return null;
        }

        var lowerTotal = primaryStates[1].Ratio;
        var lowerRatioTotal = lowerStates[0].Ratio + lowerStates[1].Ratio;
        if (!double.IsFinite(lowerTotal) ||
            lowerTotal <= 0 ||
            !double.IsFinite(lowerRatioTotal) ||
            lowerRatioTotal <= 0)
        {
            return null;
        }

        return new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference(
                SessionLayoutPaneIds.Media,
                primaryStates[0].Ratio,
                primaryStates[0].IsCollapsed),
            new SessionPaneSizePreference(
                SessionLayoutPaneIds.Map,
                lowerTotal * (lowerStates[0].Ratio / lowerRatioTotal),
                lowerStates[0].IsCollapsed),
            new SessionPaneSizePreference(
                SessionLayoutPaneIds.ExtensionMedia,
                lowerTotal * (lowerStates[1].Ratio / lowerRatioTotal),
                lowerStates[1].IsCollapsed),
        ]);
    }

    private IReadOnlyList<string> GetVisiblePaneIds()
    {
        var paneIds = new List<string>(capacity: 3);
        if (HasMediaPane())
        {
            paneIds.Add(SessionLayoutPaneIds.Media);
        }

        if (HasMapPane())
        {
            paneIds.Add(SessionLayoutPaneIds.Map);
        }

        if (HasExtensionMediaPane())
        {
            paneIds.Add(SessionLayoutPaneIds.ExtensionMedia);
        }

        return paneIds;
    }

    private bool HasMediaPane() => workspace?.MediaPaneState.ReservesLayout == true;

    private bool HasMapPane() => workspace?.MapState.ReservesLayout == true;

    private bool HasExtensionMediaPane() => workspace?.ExtensionSlots.MediaPanes.Count > 0;

    private static SessionPaneSizePreference ToSizePreference(SessionPaneStatePreference state) =>
        new(state.PaneId, state.Ratio, state.IsCollapsed);
}
