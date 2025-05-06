using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Sufni.App.Views;

namespace Sufni.App.DesktopViews;

public partial class MainPagesDesktopView : MainPagesViewBase
{
    public MainPagesDesktopView()
    {
        InitializeComponent();
        MenuPanel.Loaded += (s, e) =>
        {
            var menuItems = MenuPanel.GetVisualDescendants().OfType<MenuItem>();
            foreach (var menuItem in menuItems)
            {
                menuItem.PointerPressed += (s, e) =>
                {
                    MainSplitView.IsPaneOpen = false;
                };
            }
        };
    }
}