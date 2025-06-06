using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Sufni.App.Views;

namespace Sufni.App.DesktopViews;

public partial class MainPagesDesktopView : MainPagesViewBase
{
    public MainPagesDesktopView()
    {
        InitializeComponent();

        // Catch taps outside MenuPanel, so that we can close it
        MenuPanelBackground.Tapped += (s, e) => MenuPanel.IsVisible = false;

        // Close MenuPanel when one of the options is selected
        MenuPanel.Loaded += (s, e) =>
        {
            var menuItems = MenuPanel.GetLogicalDescendants().OfType<MenuItem>();
            foreach (var menuItem in menuItems)
            {
                menuItem.PointerPressed += (s, e) =>
                {
                    MenuPanel.IsVisible = false;
                };
            }
        };
        // Allow the pane to close/open on tab header clicks
        var pagesTabItems = PagesMenu.GetLogicalChildren();
        foreach (var item in pagesTabItems)
        {
            var tabItem = item as TabItem;
            Debug.Assert(tabItem is not null);

            tabItem.PointerPressed += (_, _) =>
            {
                var splitView = PagesMenu.FindAncestorOfType<SplitView>();
                Debug.Assert(splitView is not null);

                splitView.IsPaneOpen = tabItem != PagesMenu.SelectedItem || !splitView.IsPaneOpen;
            };
        }
    }
}