using System;
using System.Linq;
using Sufni.App.ViewModels;

namespace Sufni.App.Coordinators;

public sealed class DesktopShellCoordinator(Func<IMainWindowShellHost> mainWindowProvider) : IShellCoordinator
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
            mainWindowProvider().CloseTabPage(tab, rememberForRestore: true);
        }
    }

    public void CloseIfOpen<T>(Func<T, bool> match, bool forgetRestoreHistory = false) where T : ViewModelBase
    {
        var window = mainWindowProvider();
        var existing = window.Tabs.OfType<T>().FirstOrDefault(match);
        if (forgetRestoreHistory)
        {
            window.ForgetTabHistory(match);
        }

        if (existing is TabPageViewModelBase tab)
        {
            window.CloseTabPage(tab, rememberForRestore: !forgetRestoreHistory);
        }
    }

    public void GoBack()
    {
        // Desktop has no back stack.
    }
}
