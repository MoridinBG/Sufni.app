using System;
using System.Linq;
using System.Threading.Tasks;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Shell.Coordinators;

public sealed class ShellWorkspaceCoordinator(
    ShellWorkspaceViewModel workspace,
    IAppEnvironment appEnvironment) : IShellCoordinator
{
    private bool ShouldRememberClosedTabs => appEnvironment.LayoutProfile == UiLayoutProfile.Workspace;

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
            workspace.CloseTab(tab, rememberForRestore: ShouldRememberClosedTabs);
        }
    }

    public async Task CloseIfOpen<T>(Func<T, bool> match, bool forgetRestoreHistory = false) where T : ViewModelBase
    {
        ArgumentNullException.ThrowIfNull(match);

        var existing = workspace.Tabs.OfType<T>().FirstOrDefault(match);
        if (forgetRestoreHistory)
        {
            workspace.ForgetTabHistory(match);
        }

        if (existing is TabPageViewModelBase tab)
        {
            await tab.PrepareCloseAsync();
            workspace.CloseTab(tab, rememberForRestore: !forgetRestoreHistory && ShouldRememberClosedTabs);
        }
    }

    public bool GoBack() => workspace.GoBack();
}
