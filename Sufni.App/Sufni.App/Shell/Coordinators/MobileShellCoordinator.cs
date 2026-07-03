using System;

using Sufni.App.Shared.Base;
namespace Sufni.App.Shell.Coordinators;

public sealed class MobileShellCoordinator(IMobileNavigationShellHost navigationHost) : IShellCoordinator
{
    public void Open(ViewModelBase view) => navigationHost.Push(view);

    // Mobile navigation is a stack — there is no concept of an
    // already-open editor to focus, so the factory always runs and the
    // new view is pushed.
    public void OpenOrFocus<T>(Func<T, bool> match, Func<T> create) where T : ViewModelBase
        => navigationHost.Push(create());

    public void OpenInBackground<T>(Func<T, bool> match, Func<T> create) where T : ViewModelBase
    {
    }

    public void Close(ViewModelBase view) => navigationHost.Close(view);

    // On mobile a list page and an editor for one of its rows are not on
    // the back stack at the same time, so there is nothing to close.
    public void CloseIfOpen<T>(Func<T, bool> match, bool forgetRestoreHistory = false) where T : ViewModelBase
    {
    }

    public bool GoBack() => navigationHost.Pop();
}
