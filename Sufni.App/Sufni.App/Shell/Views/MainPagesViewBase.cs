using Avalonia.Controls;
using Avalonia;
using Avalonia.VisualTree;
using System.Linq;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Shell.Views;

public partial class MainPagesViewBase : UserControl
{
    public static readonly StyledProperty<MainPagesViewModel?> MainPagesProperty =
        AvaloniaProperty.Register<MainPagesViewBase, MainPagesViewModel?>(nameof(MainPages));

    public MainPagesViewModel? MainPages
    {
        get => GetValue(MainPagesProperty);
        set => SetValue(MainPagesProperty, value);
    }

    protected static void RegisterDrawerMenuAutoClose(Control menuPanel, DrawerPage drawerPage)
    {
        menuPanel.Loaded += (_, _) =>
        {
            var menuItems = menuPanel.GetVisualDescendants().OfType<MenuItem>();
            foreach (var menuItem in menuItems)
            {
                menuItem.PointerPressed += (_, _) =>
                {
                    drawerPage.IsOpen = false;
                };
            }
        };
    }
}
