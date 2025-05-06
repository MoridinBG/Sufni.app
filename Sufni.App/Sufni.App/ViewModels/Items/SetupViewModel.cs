using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.ViewModels.Items;

public partial class SetupViewModel : ItemViewModelBase
{
    private Setup setup;
    private string? originalBoardId;
    public bool IsInDatabase;

    #region Observable properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private string? boardId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private BikeViewModel? selectedBike;

    /* TODO: remove
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private CalibrationViewModel? selectedFrontCalibration;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private CalibrationViewModel? selectedRearCalibration;

    public ReadOnlyObservableCollection<ItemViewModelBase> Linkages => linkages;
    private readonly ReadOnlyObservableCollection<ItemViewModelBase> linkages;

    public ReadOnlyObservableCollection<ItemViewModelBase> Calibrations => calibrations;
    private readonly ReadOnlyObservableCollection<ItemViewModelBase> calibrations;
    */

    #endregion

    #region Constructors

    public SetupViewModel()
    {
        setup = new Setup();
        Id = setup.Id;
        BoardId = originalBoardId = boardId;
    }

    public SetupViewModel(Setup setup, string? boardId, bool fromDatabase)
    {
        this.setup = setup;
        IsInDatabase = fromDatabase;
        Id = setup.Id;
        BoardId = originalBoardId = boardId;

        ResetImplementation();
    }

    #endregion

    #region ItemViewModelBase overrides

    protected override void EvaluateDirtiness()
    {
        IsDirty =
            !IsInDatabase ||
            Name != setup.Name ||
            BoardId != originalBoardId;
    }

    protected override bool CanSave()
    {
        EvaluateDirtiness();
        return IsDirty;
    }

    protected override async Task SaveImplementation()
    {
        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var newSetup = new Setup(
                Id,
                Name ?? $"setup #{Id}");
            Id = await databaseService.PutSetupAsync(newSetup);

            // If this setup was already associated with another board, clear that association.
            // Do not delete the board though, it might be picked up later.
            if (!string.IsNullOrEmpty(originalBoardId) && IsInDatabase && originalBoardId != BoardId)
            {
                await databaseService.PutBoardAsync(new Board(originalBoardId, null));
            }

            // If the board ID changed, or this is a new setup, associate it with the board ID.
            if (!string.IsNullOrEmpty(BoardId) && (!IsInDatabase || originalBoardId != BoardId))
            {
                await databaseService.PutBoardAsync(new Board(BoardId!, Id));
            }

            setup = newSetup;
            originalBoardId = BoardId;

            SaveCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();

            // We notify even if the setup was already in the database, since we need to reevaluate
            // if a setup exists for the import page.
            var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
            Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");
            mainPagesViewModel.SetupsPage.OnAdded(this);
            await mainPagesViewModel.ImportSessionsPage.EvaluateSetupExists();

            IsInDatabase = true;

            OpenPreviousPage();
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Setup could not be saved: {e.Message}");
        }
    }

    protected override Task ResetImplementation()
    {
        Name = setup.Name;
        BoardId = originalBoardId;

        return Task.CompletedTask;
    }

    #endregion
}