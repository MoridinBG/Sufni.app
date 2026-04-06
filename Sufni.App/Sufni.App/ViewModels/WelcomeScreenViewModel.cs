using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class WelcomeScreenViewModel : TabPageViewModelBase
{
    private readonly IBikeCoordinator bikeCoordinator;
    private readonly ISetupCoordinator setupCoordinator;
    private readonly IImportSessionsOpener importSessionsOpener;

    #region Constructors

    public WelcomeScreenViewModel()
    {
        bikeCoordinator = null!;
        setupCoordinator = null!;
        importSessionsOpener = null!;
        Name = "Welcome";
    }

    public WelcomeScreenViewModel(
        INavigator navigator,
        IDialogService dialogService,
        IBikeCoordinator bikeCoordinator,
        ISetupCoordinator setupCoordinator,
        IImportSessionsOpener importSessionsOpener)
        : base(navigator, dialogService)
    {
        this.bikeCoordinator = bikeCoordinator;
        this.setupCoordinator = setupCoordinator;
        this.importSessionsOpener = importSessionsOpener;
        Name = "Welcome";
    }

    #endregion Constructors

    #region Commands

    [RelayCommand]
    private async Task AddBike() => await bikeCoordinator.OpenCreateAsync();

    [RelayCommand]
    private async Task AddSetup() => await setupCoordinator.OpenCreateAsync();

    [RelayCommand]
    private void ImportSession() => importSessionsOpener.OpenImportSessions();

    #endregion Commands
}
