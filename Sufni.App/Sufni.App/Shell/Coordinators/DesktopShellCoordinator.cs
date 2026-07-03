using System;
using System.Linq;

using Sufni.App.Shared.Base;
using Sufni.App.Shell.ViewModels;
namespace Sufni.App.Shell.Coordinators;

public sealed class DesktopShellCoordinator(Func<IMainWindowShellHost> mainWindowProvider) : IShellCoordinator
{
    public void Open(ViewModelBase view) => mainWindowProvider().OpenView(view);

    public void OpenOrFocus<T>(Func<T, bool> match, Func<T> create) where T : ViewModelBase
    {
        var window = mainWindowProvider();
        var existing = window.Tabs.OfType<T>().FirstOrDefault(match);
        if (existing is not null)
        {
            window.OpenView(existing);
            return;
        }

        window.OpenView(window.TakeTabHistory(match) ?? create());
    }

    public void OpenInBackground<T>(Func<T, bool> match, Func<T> create) where T : ViewModelBase
    {
        var window = mainWindowProvider();
        if (window.Tabs.OfType<T>().Any(match))
        {
            return;
        }

        window.AddView(window.TakeTabHistory(match) ?? create());
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

    public bool GoBack()
    {
        // Desktop has no back stack.
        return false;
    }
}
