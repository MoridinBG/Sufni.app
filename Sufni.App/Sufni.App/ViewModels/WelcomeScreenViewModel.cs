using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class WelcomeScreenViewModel : TabPageViewModelBase
{
    private readonly IBikeCoordinator bikeCoordinator;
    private readonly ISetupCoordinator setupCoordinator;
    private readonly IImportSessionsCoordinator importSessionsCoordinator;
    private readonly IFilesService filesService;

    public bool IsDesktop => App.Current?.IsDesktop == true;

    #region Constructors

    public WelcomeScreenViewModel()
    {
        bikeCoordinator = null!;
        setupCoordinator = null!;
        importSessionsCoordinator = null!;
        filesService = null!;
        Name = "Welcome";
    }

    public WelcomeScreenViewModel(
        IShellCoordinator shell,
        IDialogService dialogService,
        IBikeCoordinator bikeCoordinator,
        ISetupCoordinator setupCoordinator,
        IImportSessionsCoordinator importSessionsCoordinator,
        IFilesService filesService)
        : base(shell, dialogService)
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
