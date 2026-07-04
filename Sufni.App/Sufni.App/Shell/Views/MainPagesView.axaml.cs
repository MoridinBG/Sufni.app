namespace Sufni.App.Shell.Views;

public partial class MainPagesView : MainPagesViewBase
{
    public MainPagesView()
    {
        InitializeComponent();
        RegisterDrawerMenuAutoClose(MenuPanel, MainDrawerPage);
    }
}
