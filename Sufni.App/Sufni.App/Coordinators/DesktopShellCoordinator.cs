using System;
using System.Linq;
using Sufni.App.ViewModels;

namespace Sufni.App.Coordinators;

public sealed class DesktopShellCoordinator(Func<MainWindowViewModel> mainWindowProvider) : IShellCoordinator
{
    public void Open(ViewModelBase view) => mainWindowProvider().OpenView(view);

    public void OpenOrFocus<T>(Func<T, bool> match, Func<T> create) where T : ViewModelBase
    {
        var window = mainWindowProvider();
        var existing = window.Tabs.OfType<T>().FirstOrDefault(match);
        window.OpenView(existing ?? create());
    }

    public void Close(ViewModelBase view)
    {
        if (view is TabPageViewModelBase tab)
        {
            mainWindowProvider().CloseTabPage(tab);
        }
    }

    public void GoBack()
    {
        // Desktop has no back stack.
    }
}
