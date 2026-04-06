namespace Sufni.App.ViewModels;

public interface INavigator
{
    void OpenPage(ViewModelBase view);
    void OpenPreviousPage();
    void CloseTab(TabPageViewModelBase tab);
}
