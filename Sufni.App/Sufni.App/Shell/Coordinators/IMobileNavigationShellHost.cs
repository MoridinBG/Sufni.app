using System.Collections.Generic;

using Sufni.App.Shared.Base;
namespace Sufni.App.Shell.Coordinators;

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
