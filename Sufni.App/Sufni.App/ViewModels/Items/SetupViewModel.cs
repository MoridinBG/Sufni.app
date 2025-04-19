using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
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
    private LinkageViewModel? selectedLinkage;

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

    #endregion

    #region Constructors

    public SetupViewModel()
    {
        setup = new Setup();
        Id = setup.Id;
        BoardId = originalBoardId = boardId;
        linkages = new ReadOnlyObservableCollection<ItemViewModelBase>([]);
        calibrations = new ReadOnlyObservableCollection<ItemViewModelBase>([]);
    }

    public SetupViewModel(Setup setup, string? boardId, bool fromDatabase,
        SourceCache<ItemViewModelBase, Guid> linkagesSourceCache,
        SourceCache<ItemViewModelBase, Guid> calibrationsSourceCache)
    {
        this.setup = setup;
        IsInDatabase = fromDatabase;
        Id = setup.Id;
        BoardId = originalBoardId = boardId;

        linkagesSourceCache.Connect()
            .Bind(out linkages)
            .DisposeMany()
            .Subscribe();

        calibrationsSourceCache.Connect()
            .Bind(out calibrations)
            .DisposeMany()
            .Subscribe();

        ResetImplementation();
    }

    #endregion

    #region ItemViewModelBase overrides

    protected override void EvaluateDirtiness()
    {
        IsDirty =
            !IsInDatabase ||
            Name != setup.Name ||
            BoardId != originalBoardId ||
            SelectedLinkage == null || SelectedLinkage.Id != setup.LinkageId ||
            SelectedFrontCalibration?.Id != setup.FrontCalibrationId ||
            SelectedRearCalibration?.Id != setup.RearCalibrationId;
    }

    protected override bool CanSave()
    {
        EvaluateDirtiness();
        return IsDirty && !(SelectedFrontCalibration == null && SelectedRearCalibration == null);
    }

    protected override async Task SaveImplementation()
    {
        Debug.Assert(SelectedLinkage != null, nameof(SelectedLinkage) + " != null");
        Debug.Assert(!(SelectedFrontCalibration == null && SelectedRearCalibration == null),
            nameof(SelectedFrontCalibration) + " and " + nameof(SelectedRearCalibration) + " can't be both null");

        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var newSetup = new Setup(
                Id,
                Name ?? $"setup #{Id}",
                SelectedLinkage.Id,
                SelectedFrontCalibration?.Id,
                SelectedRearCalibration?.Id);
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
        try
        {
            Name = setup.Name;
            BoardId = originalBoardId;
            SelectedLinkage = Linkages.First(l => l.Id == setup.LinkageId) as LinkageViewModel;
            SelectedFrontCalibration = Calibrations.FirstOrDefault(c => c?.Id == setup.FrontCalibrationId, null) as CalibrationViewModel;
            SelectedRearCalibration = Calibrations.FirstOrDefault(c => c?.Id == setup.RearCalibrationId, null) as CalibrationViewModel;
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Setup could not be reset: {e.Message}");
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void AddLinkage()
    {
        var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");

        mainPagesViewModel.LinkagesPage.AddCommand.Execute(null);
    }

    [RelayCommand]
    private void AddCalibration()
    {
        var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");

        mainPagesViewModel.CalibrationsPage.AddCommand.Execute(null);
    }

    #endregion
}