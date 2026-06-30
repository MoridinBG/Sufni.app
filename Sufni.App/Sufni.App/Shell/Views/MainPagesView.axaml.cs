using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Linq;

namespace Sufni.App.Shell.Views;

public partial class MainPagesView : MainPagesViewBase
{
    public MainPagesView()
    {
        InitializeComponent();
        MenuPanel.Loaded += (s, e) =>
        {
            var menuItems = MenuPanel.GetVisualDescendants().OfType<MenuItem>();
            foreach (var menuItem in menuItems)
            {
                menuItem.PointerPressed += (s, e) =>
                {
                    MainDrawerPage.IsOpen = false;
                };
            }
        };
    }
}
