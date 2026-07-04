using System;
using System.Linq;

using Sufni.App.Shared.Base;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Shell.Coordinators;

public sealed class ShellWorkspaceCoordinator(ShellWorkspaceViewModel workspace) : IShellCoordinator
{
    public void Open(ViewModelBase view)
    {
        if (view is TabPageViewModelBase tab)
        {
            workspace.OpenOrFocus(tab);
        }
    }

    public void OpenOrFocus<T>(Func<T, bool> match, Func<T> create) where T : ViewModelBase
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(create);

        var existing = workspace.Tabs.OfType<T>().FirstOrDefault(match);
        if (existing is TabPageViewModelBase existingTab)
        {
            workspace.OpenOrFocus(existingTab);
            return;
        }

        if (workspace.TakeTabHistory(match) is TabPageViewModelBase restoredTab)
        {
            workspace.OpenOrFocus(restoredTab);
            return;
        }

        Open(create());
    }

    public void OpenInBackground<T>(Func<T, bool> match, Func<T> create) where T : ViewModelBase
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(create);

        if (workspace.Tabs.OfType<T>().Any(match))
        {
            return;
        }

        if (workspace.TakeTabHistory(match) is TabPageViewModelBase restoredTab)
        {
            workspace.OpenInBackground(restoredTab);
            return;
        }

        if (create() is TabPageViewModelBase tab)
        {
            workspace.OpenInBackground(tab);
        }
    }

    public void Close(ViewModelBase view)
    {
        if (view is TabPageViewModelBase tab)
        {
            workspace.CloseTab(tab, rememberForRestore: true);
        }
    }

    public void CloseIfOpen<T>(Func<T, bool> match, bool forgetRestoreHistory = false) where T : ViewModelBase
    {
        ArgumentNullException.ThrowIfNull(match);

        var existing = workspace.Tabs.OfType<T>().FirstOrDefault(match);
        if (forgetRestoreHistory)
        {
            workspace.ForgetTabHistory(match);
        }

        if (existing is TabPageViewModelBase tab)
        {
            workspace.CloseTab(tab, rememberForRestore: !forgetRestoreHistory);
        }
    }

    public bool GoBack() => workspace.GoBack();
}
