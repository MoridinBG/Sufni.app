namespace Sufni.App.ViewModels;

public class DesktopNavigator(MainWindowViewModel mainWindowViewModel) : INavigator
{
    public void OpenPage(ViewModelBase view) => mainWindowViewModel.OpenView(view);
    public void OpenPreviousPage() { }
    public void CloseTab(TabPageViewModelBase tab) => mainWindowViewModel.CloseTabPage(tab);
}
