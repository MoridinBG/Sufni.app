namespace Sufni.App.ViewModels;

public class MobileNavigator(MainViewModel mainViewModel) : INavigator
{
    public void OpenPage(ViewModelBase view) => mainViewModel.OpenView(view);
    public void OpenPreviousPage() => mainViewModel.OpenPreviousView();
    public void CloseTab(TabPageViewModelBase tab) { }
}
