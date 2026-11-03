using Sufni.App.Sessions.Detail.DesktopViews.Controls;
using Sufni.App.Sessions.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Detail.DesktopViews.Controls;

[Collection("Ui")]
public class CollapsibleSplitViewTests
{
    [AvaloniaFact]
    public async Task CollapsibleSplitView_ShowsExpandedTouchTarget_WhenInheritedTouchInputChanges()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var view = CreateView();
        var root = new Grid
        {
            Children = { view },
        };
        var host = await ViewTestHelpers.ShowViewAsync(root);
        await using var mounted = new MountedView<CollapsibleSplitView>(host, view);
        var touchTarget = FindPart<Border>(mounted.View, "PART_SplitTouchTarget");

        Assert.False(touchTarget.IsVisible);
        Assert.False(touchTarget.IsHitTestVisible);

        CollapsibleSplitView.SetHasTouchInput(root, true);

        Assert.True(touchTarget.IsVisible);
        Assert.True(touchTarget.IsHitTestVisible);

        CollapsibleSplitView.SetHasTouchInput(root, false);

        Assert.False(touchTarget.IsVisible);
        Assert.False(touchTarget.IsHitTestVisible);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_ClickingCollapsedHeader_RestoresStoredPaneRatio()
    {
        var view = CreateView();
        view.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference("first", 0.2, IsCollapsed: true),
            new SessionPaneSizePreference("second", 0.8),
        ]);

        await using var mounted = await MountAsync(view);

        var header = FindPart<Button>(mounted.View, "PART_FirstCollapsedHeader");
        Assert.False(FindPart<ContentControl>(mounted.View, "PART_FirstContentHost").IsEffectivelyVisible);
        var point = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), mounted.Host);
        Assert.NotNull(point);
        mounted.Host.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        var firstHost = FindPart<ContentControl>(mounted.View, "PART_FirstContentHost");
        Assert.True(firstHost.IsVisible);
        var secondHost = FindPart<ContentControl>(mounted.View, "PART_SecondContentHost");
        Assert.True(firstHost.IsEffectivelyVisible);
        Assert.InRange(firstHost.Bounds.Width / (firstHost.Bounds.Width + secondHost.Bounds.Width), 0.195, 0.205);
        Assert.NotNull(mounted.View.Preferences);
        AssertPane(mounted.View.Preferences!, "first", 0.2, isCollapsed: false);
        AssertPane(mounted.View.Preferences!, "second", 0.8, isCollapsed: false);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_FiniteResize_DoesNotExpandToOversizedChildDesiredWidth()
    {
        var view = CreateView();
        view.DefaultFirstLength = new GridLength(1, GridUnitType.Star);
        view.DefaultSecondLength = new GridLength(400);
        view.FirstContent = new Border { MinWidth = 1600 };

        await using var mounted = await MountAsync(view);

        Resize(mounted.View, width: 1800, height: 700);
        Resize(mounted.View, width: 900, height: 700);

        Assert.Equal(900, mounted.View.DesiredSize.Width);
        Assert.Equal(900, mounted.View.Bounds.Width);
        var secondHost = FindPart<ContentControl>(mounted.View, "PART_SecondContentHost");
        Assert.True(secondHost.IsEffectivelyVisible);
        Assert.True(secondHost.Bounds.Width > 0);
    }

    private static CollapsibleSplitView CreateView()
    {
        return new CollapsibleSplitView
        {
            FirstPaneId = "first",
            SecondPaneId = "second",
            FirstPaneLabel = "First",
            SecondPaneLabel = "Second",
            FirstContent = new Border(),
            SecondContent = new Border(),
        };
    }

    private static async Task<MountedView<CollapsibleSplitView>> MountAsync(CollapsibleSplitView view)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedView<CollapsibleSplitView>(host, view);
    }

    private static T FindPart<T>(CollapsibleSplitView view, string name)
        where T : Control
    {
        return view.GetVisualDescendants()
            .OfType<T>()
            .Single(control => control.Name == name);
    }

    private static void AssertPane(
        SessionPaneGroupPreferences preferences,
        string paneId,
        double ratio,
        bool isCollapsed)
    {
        var pane = Assert.Single(preferences.Panes, pane => pane.PaneId == paneId);
        Assert.Equal(ratio, pane.Ratio, precision: 6);
        Assert.Equal(isCollapsed, pane.IsCollapsed);
    }

    private static void Resize(Control control, double width, double height)
    {
        control.Measure(new Avalonia.Size(width, height));
        control.Arrange(new Avalonia.Rect(0, 0, width, height));
    }
}
