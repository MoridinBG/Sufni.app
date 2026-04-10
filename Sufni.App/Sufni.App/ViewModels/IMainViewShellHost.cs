namespace Sufni.App.ViewModels;

/// <summary>
/// Narrow shell-host abstraction over <see cref="MainViewModel"/>
/// exposing only the members <see cref="Coordinators.MobileShellCoordinator"/>
/// actually depends on. Exists so the mobile shell coordinator can be
/// unit-tested against an NSubstitute substitute instead of a real
/// view model with non-virtual stack-mutating methods.
/// </summary>
public interface IMainViewShellHost
{
    ViewModelBase CurrentView { get; }
    void OpenView(ViewModelBase view);
    void OpenPreviousView();
}
