using System.Collections.Generic;
using Sufni.App.ViewModels;

namespace Sufni.App.Coordinators;

public interface IMobileNavigationShellHost
{
    ViewModelBase CurrentView { get; }
    bool CanGoBack { get; }
    IReadOnlyList<ViewModelBase> LogicalStack { get; }

    void SetRoot(ViewModelBase root);
    void Push(ViewModelBase viewModel);
    bool Pop();
    bool Close(ViewModelBase viewModel);
}
