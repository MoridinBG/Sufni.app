using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Models;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.ItemLists;

public class SetupListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly ImportSessionsViewModel importSessionsPage;
    private readonly BikeListViewModel bikesPage;

    #endregion Private fields

    #region Observable properties

    private ObservableCollection<Board> Boards { get; } = [];

    #endregion Observable properties

    #region Constructors

    public SetupListViewModel()
    {
        importSessionsPage = new ImportSessionsViewModel();
        bikesPage = new BikeListViewModel();
    }

    public SetupListViewModel(ImportSessionsViewModel importSessionsPage, BikeListViewModel bikesPage)
    {
        this.importSessionsPage = importSessionsPage;
        this.bikesPage =  bikesPage;
    }

    #endregion Constructors

    #region Private methods

    private async Task LoadBoardsAsync()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var boards = await databaseService.GetBoardsAsync();

            foreach (var board in boards)
            {
                Boards.Add(board);
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not load Boards: {e.Message}");
        }
    }

    private static void OnSetupDirtinessChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupViewModel.IsDirty) && sender is SetupViewModel { IsDirty: false } svm)
        {
            svm.SelectedBike?.DeleteCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task LoadSetupsAsync()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var setupList = await databaseService.GetSetupsAsync();
            foreach (var setup in setupList)
            {
                var board = Boards.FirstOrDefault(b => b?.SetupId == setup.Id, null);
                var svm = new SetupViewModel(
                    setup,
                    board?.Id,
                    true,
                    bikesPage.Source);
                svm.PropertyChanged += OnSetupDirtinessChanged;
                Source.AddOrUpdate(svm);
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not load Setups: {e.Message}");
        }
    }

    #endregion Private methods

    #region ItemListViewModelBase overrides

    protected override async Task DeleteImplementation(ItemViewModelBase vm)
    {
        var svm = vm as SetupViewModel;
        Debug.Assert(svm != null, nameof(svm) + " != null");
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        // If this setup is associated with a board ID, clear that association.
        if (svm.BoardId.HasValue)
        {
            await databaseService.PutBoardAsync(new Board(svm.BoardId.Value, null));
        }

        // Notify associated calibrations and linkages about the deletion
        await databaseService.DeleteSetupAsync(vm.Id);
        svm.SelectedBike?.DeleteCommand.NotifyCanExecuteChanged();
    }

    public override async Task LoadFromDatabase()
    {
        Source.Clear();
        Boards.Clear();
        await LoadBoardsAsync();
        await LoadSetupsAsync();
    }

    protected override void AddImplementation()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var setup = new Setup(
                Guid.NewGuid(),
                "new setup");

            // Use the SST datastore's board ID only if it's not already associated to another setup;
            Guid? newSetupsBoardId = null;
            var datastoreBoardId = importSessionsPage.SelectedDataStore?.BoardId;
            var datastoreBoard = Boards.FirstOrDefault(b =>
                b?.Id == datastoreBoardId && b?.SetupId is not null, null);
            if (datastoreBoard is null || datastoreBoard.SetupId is null)
            {
                newSetupsBoardId = datastoreBoardId;
            }

            var svm = new SetupViewModel(setup, newSetupsBoardId, false, bikesPage.Source)
            {
                IsDirty = true
            };
            svm.PropertyChanged += OnSetupDirtinessChanged;

            OpenPage(svm);
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not add Setup: {e.Message}");
        }
    }

    #endregion ItemListViewModelBase overrides
}
