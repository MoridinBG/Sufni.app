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
            OpenTab(tab, restoreEntry: null, background: false);
        }
    }

    public void OpenOrFocus<T>(
        Func<T, bool> match,
        Func<T> create,
        ClosedTabRestoreEntry? restoreEntry = null)
        where T : ViewModelBase
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(create);

        var existing = workspace.Tabs.OfType<T>().FirstOrDefault(match);
        if (existing is TabPageViewModelBase existingTab)
        {
            workspace.OpenOrFocus(existingTab, restoreEntry);
            return;
        }

        if (TryRestoreFromHistory(restoreEntry, create) is { } restoredTab)
        {
            OpenTab(restoredTab, restoreEntry, background: false);
            return;
        }

        OpenTab(create(), restoreEntry, background: false);
    }

    public void OpenInBackground<T>(
        Func<T, bool> match,
        Func<T> create,
        ClosedTabRestoreEntry? restoreEntry = null)
        where T : ViewModelBase
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(create);

        if (workspace.Tabs.OfType<T>().Any(match))
        {
            return;
        }

        if (TryRestoreFromHistory(restoreEntry, create) is { } restoredTab)
        {
            OpenTab(restoredTab, restoreEntry, background: true);
            return;
        }

        OpenTab(create(), restoreEntry, background: true);
    }

    public void Close(ViewModelBase view)
    {
        if (view is TabPageViewModelBase tab)
        {
            workspace.CloseTab(tab, ShouldRememberClosedTabs);
        }
    }

    public async Task CloseIfOpen<T>(
        Func<T, bool> match,
        bool forgetRestoreHistory = false,
        object? restoreKey = null)
        where T : ViewModelBase
    {
        ArgumentNullException.ThrowIfNull(match);

        var existing = workspace.Tabs.OfType<T>().FirstOrDefault(match);
        if (forgetRestoreHistory && restoreKey is not null)
        {
            workspace.ForgetTabHistory(typeof(T), restoreKey);
        }

        if (existing is TabPageViewModelBase tab)
        {
            await tab.PrepareCloseAsync();
            var rememberForRestore =
                !forgetRestoreHistory &&
                ShouldRememberClosedTabs;
            workspace.CloseTab(tab, rememberForRestore);
        }
    }

    public bool GoBack() => workspace.GoBack();

    private void OpenTab(
        ViewModelBase view,
        ClosedTabRestoreEntry? restoreEntry,
        bool background)
    {
        if (view is not TabPageViewModelBase tab)
        {
            return;
        }

        if (background)
        {
            workspace.OpenInBackground(tab, restoreEntry);
            return;
        }

        workspace.OpenOrFocus(tab, restoreEntry);
    }

    private TabPageViewModelBase? TryRestoreFromHistory<T>(
        ClosedTabRestoreEntry? restoreEntry,
        Func<T> fallbackCreate)
        where T : ViewModelBase
    {
        if (restoreEntry is null)
        {
            return null;
        }

        var closedEntry = workspace.TakeTabHistory(restoreEntry.TabType, restoreEntry.Key);
        if (closedEntry is null)
        {
            return null;
        }

        return closedEntry.Restore() ?? fallbackCreate() as TabPageViewModelBase;
    }
}
