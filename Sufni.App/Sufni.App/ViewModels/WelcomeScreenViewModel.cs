using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.ExtensionHost.Services;

namespace Sufni.App.ViewModels;

public partial class WelcomeScreenViewModel : TabPageViewModelBase
{
    private readonly IBikeCoordinator bikeCoordinator;
    private readonly ISetupCoordinator setupCoordinator;
    private readonly IImportSessionsCoordinator importSessionsCoordinator;
    private readonly IFilesService filesService;

    #region Constructors

    public WelcomeScreenViewModel(
        IShellCoordinator shell,
        IDialogService dialogService,
        IBikeCoordinator bikeCoordinator,
        ISetupCoordinator setupCoordinator,
        IImportSessionsCoordinator importSessionsCoordinator,
        IFilesService filesService,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(shell, dialogService, uiThreadDispatcher)
    {
        this.bikeCoordinator = bikeCoordinator;
        this.setupCoordinator = setupCoordinator;
        this.importSessionsCoordinator = importSessionsCoordinator;
        this.filesService = filesService;
        Name = "Welcome";
    }

    #endregion Constructors

    #region Commands

    [RelayCommand]
    private async Task AddBike() => await bikeCoordinator.OpenCreateAsync();

    [RelayCommand]
    private async Task AddSetup() => await setupCoordinator.OpenCreateForDetectedBoardAsync();

    [RelayCommand]
    private async Task ImportSession() => await importSessionsCoordinator.OpenAsync();

    [RelayCommand]
    private async Task OpenLogsFolder() => await filesService.OpenLogsFolderAsync();

    #endregion Commands
}
