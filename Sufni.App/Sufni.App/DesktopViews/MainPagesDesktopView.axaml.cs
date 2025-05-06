using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
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
    }
}