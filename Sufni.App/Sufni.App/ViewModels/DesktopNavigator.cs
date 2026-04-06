using System;

namespace Sufni.App.ViewModels;

public class DesktopNavigator(Func<MainWindowViewModel> mainWindowViewModelProvider) : INavigator
{
    public void OpenPage(ViewModelBase view) => mainWindowViewModelProvider().OpenView(view);
    public void OpenPreviousPage() { }
    public void CloseTab(TabPageViewModelBase tab) => mainWindowViewModelProvider().CloseTabPage(tab);
}
