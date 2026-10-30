using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Sufni.App.Shell.Views;
namespace Sufni.App.Shell.DesktopViews;

public partial class MainPagesDesktopView : MainPagesViewBase
{
    public MainPagesDesktopView()
    {
        InitializeComponent();

        // Allow the pane to close/open on tab header clicks.
        foreach (var tabItem in new[] { SessionTabItem, BikeSetupsTabItem, BikesTabItem, LiveDaqsTabItem })
        {
            Debug.Assert(tabItem is not null);

            tabItem.AddHandler<PointerPressedEventArgs>(
                InputElement.PointerPressedEvent,
                OnPageTabPointerPressed,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
        }
    }

    private void OnPageTabPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        var tabItem = sender as TabItem;
        Debug.Assert(tabItem is not null);

        TogglePaneForPageTab(tabItem);
    }

    private void TogglePaneForPageTab(TabItem tabItem)
    {
        var splitView = PagesMenu.FindAncestorOfType<SplitView>();
        if (splitView is null)
        {
            return;
        }

        splitView.IsPaneOpen = tabItem != PagesMenu.SelectedItem || !splitView.IsPaneOpen;
    }
}
