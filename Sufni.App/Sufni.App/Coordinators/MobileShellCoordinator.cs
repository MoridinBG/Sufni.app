using System;
using Sufni.App.ViewModels;

namespace Sufni.App.Coordinators;

public sealed class MobileShellCoordinator(Func<MainViewModel> mainViewProvider) : IShellCoordinator
{
    public void Open(ViewModelBase view) => mainViewProvider().OpenView(view);

    // Mobile navigation is a stack — there is no concept of an
    // already-open editor to focus, so the factory always runs and the
    // new view is pushed.
    public void OpenOrFocus<T>(Func<T, bool> match, Func<T> create) where T : ViewModelBase
        => mainViewProvider().OpenView(create());

    public void Close(ViewModelBase view)
    {
        var main = mainViewProvider();
        if (ReferenceEquals(main.CurrentView, view))
        {
            main.OpenPreviousView();
        }
    }

    // On mobile a list page and an editor for one of its rows are not on
    // the back stack at the same time, so there is nothing to close.
    public void CloseIfOpen<T>(Func<T, bool> match) where T : ViewModelBase
    {
    }

    public void GoBack() => mainViewProvider().OpenPreviousView();
}
