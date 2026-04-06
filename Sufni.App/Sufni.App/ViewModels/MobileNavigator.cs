using System;

namespace Sufni.App.ViewModels;

public class MobileNavigator(Func<MainViewModel> mainViewModelProvider) : INavigator
{
    public void OpenPage(ViewModelBase view) => mainViewModelProvider().OpenView(view);
    public void OpenPreviousPage() => mainViewModelProvider().OpenPreviousView();
    public void CloseTab(TabPageViewModelBase tab) { }
}
