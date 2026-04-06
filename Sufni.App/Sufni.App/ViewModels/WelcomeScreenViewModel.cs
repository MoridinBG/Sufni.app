using CommunityToolkit.Mvvm.Input;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class WelcomeScreenViewModel : TabPageViewModelBase
{
    private readonly IShellCoordinator shellCoordinator;

    #region Constructors

    public WelcomeScreenViewModel()
    {
        shellCoordinator = null!;
        Name = "Welcome";
    }

    public WelcomeScreenViewModel(INavigator navigator, IDialogService dialogService, IShellCoordinator shellCoordinator)
        : base(navigator, dialogService)
    {
        this.shellCoordinator = shellCoordinator;
        Name = "Welcome";
    }

    #endregion Constructors

    #region Commands

    [RelayCommand]
    private void AddBike() => shellCoordinator.AddBike();

    [RelayCommand]
    private void AddSetup() => shellCoordinator.AddSetup();

    [RelayCommand]
    private void ImportSession() => shellCoordinator.OpenImportSessions();

    #endregion Commands
}
