using System;
using Sufni.App.ViewModels;

namespace Sufni.App.Coordinators;

public sealed class MobileShellCoordinator(Func<MainViewModel> mainViewProvider) : IShellCoordinator
{
    public void Open(ViewModelBase view) => mainViewProvider().OpenView(view);

    public void Close(ViewModelBase view)
    {
        var main = mainViewProvider();
        if (ReferenceEquals(main.CurrentView, view))
        {
            main.OpenPreviousView();
        }
    }

    public void GoBack() => mainViewProvider().OpenPreviousView();
}
