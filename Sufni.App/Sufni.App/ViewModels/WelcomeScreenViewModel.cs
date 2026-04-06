using CommunityToolkit.Mvvm.Input;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class WelcomeScreenViewModel : TabPageViewModelBase
{
    private readonly IBikeCreator bikeCreator;
    private readonly ISetupCreator setupCreator;
    private readonly IImportSessionsOpener importSessionsOpener;

    #region Constructors

    public WelcomeScreenViewModel()
    {
        bikeCreator = null!;
        setupCreator = null!;
        importSessionsOpener = null!;
        Name = "Welcome";
    }

    public WelcomeScreenViewModel(
        INavigator navigator,
        IDialogService dialogService,
        IBikeCreator bikeCreator,
        ISetupCreator setupCreator,
        IImportSessionsOpener importSessionsOpener)
        : base(navigator, dialogService)
    {
        this.bikeCreator = bikeCreator;
        this.setupCreator = setupCreator;
        this.importSessionsOpener = importSessionsOpener;
        Name = "Welcome";
    }

    #endregion Constructors

    #region Commands

    [RelayCommand]
    private void AddBike() => bikeCreator.AddBike();

    [RelayCommand]
    private void AddSetup() => setupCreator.AddSetup();

    [RelayCommand]
    private void ImportSession() => importSessionsOpener.OpenImportSessions();

    #endregion Commands
}
