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