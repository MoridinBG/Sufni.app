using Avalonia.Controls;

namespace Sufni.App.Shell.Views;

public partial class CompactShellView : MainPagesViewBase
{
    public CompactShellView()
    {
        InitializeComponent();
        RegisterDrawerMenuAutoClose(MenuPanel, MainDrawerPage);
        RegisterPairingServerStartup();
        RegisterPrimaryPageSelection(PagesTabbedPage);
    }
}
