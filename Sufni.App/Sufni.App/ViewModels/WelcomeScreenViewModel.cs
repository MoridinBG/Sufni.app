using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.ViewModels;

public partial class WelcomeScreenViewModel : TabPageViewModelBase
{
    #region Constructors

    public WelcomeScreenViewModel()
    {
        Name = "Welcome";
    }

    #endregion Constructors

    #region Commands

    [RelayCommand]
    private static void AddBike() => ShellCoordinator.AddBike();

    [RelayCommand]
    private static void AddSetup() => ShellCoordinator.AddSetup();

    [RelayCommand]
    private static void ImportSession() => ShellCoordinator.OpenImportSessions();

    #endregion Commands
}
