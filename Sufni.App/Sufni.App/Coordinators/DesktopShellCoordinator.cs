using System;
using Sufni.App.ViewModels;

namespace Sufni.App.Coordinators;

public sealed class DesktopShellCoordinator(Func<MainWindowViewModel> mainWindowProvider) : IShellCoordinator
{
    public void Open(ViewModelBase view) => mainWindowProvider().OpenView(view);

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
