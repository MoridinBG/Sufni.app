using System.Collections.Generic;

namespace Sufni.App.ViewModels;

/// <summary>
/// Narrow shell-host abstraction over <see cref="MainWindowViewModel"/>
/// exposing only the members <see cref="Coordinators.DesktopShellCoordinator"/>
/// actually depends on. Exists so the desktop shell coordinator can be
/// unit-tested against an NSubstitute substitute instead of a real
/// view model with a non-substitutable observable tab collection.
/// </summary>
public interface IMainWindowShellHost
{
    IEnumerable<TabPageViewModelBase> Tabs { get; }
    void OpenView(ViewModelBase view);
    void CloseTabPage(TabPageViewModelBase tab);
}
