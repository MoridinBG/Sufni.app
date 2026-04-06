using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels.Factories;
using Sufni.App.ViewModels.Hosts;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.ItemLists;

public class SetupListViewModel : ItemListViewModelBase, ISetupViewModelHost
{
    #region Runtime host callbacks

    public Func<Task>? AfterSetupSavedCallback { get; set; }

    public void OnSetupSaved(SetupViewModel vm) => OnAdded(vm);

    public Task AfterSetupSavedAsync() => AfterSetupSavedCallback?.Invoke() ?? Task.CompletedTask;

    #endregion Runtime host callbacks

    #region Private fields

    private readonly ISetupViewModelFactory setupViewModelFactory;
    private readonly ImportSessionsViewModel importSessionsPage;
    private readonly BikeListViewModel bikesPage;

    #endregion Private fields

    #region Observable properties

    private ObservableCollection<Board> Boards { get; } = [];

    #endregion Observable properties

    #region Constructors

    public SetupListViewModel()
    {
        setupViewModelFactory = null!;
        importSessionsPage = null!;
        bikesPage = null!;
    }

    public SetupListViewModel(
        IDatabaseService databaseService,
        ISetupViewModelFactory setupViewModelFactory,
        ImportSessionsViewModel importSessionsPage,
        BikeListViewModel bikesPage,
        INavigator navigator) : base(databaseService, navigator)
    {
        this.setupViewModelFactory = setupViewModelFactory;
        this.importSessionsPage = importSessionsPage;
        this.bikesPage = bikesPage;
    }

    #endregion Constructors

    #region Private methods

    private async Task LoadBoardsAsync()
    {


        try
        {
            var boards = await databaseService.GetAllAsync<Board>();

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
        try
        {
            var setupList = await databaseService.GetAllAsync<Setup>();
            foreach (var setup in setupList)
            {
                var board = Boards.FirstOrDefault(b => b?.SetupId == setup.Id, null);
                var svm = setupViewModelFactory.Create(
                    setup,
                    board?.Id,
                    true,
                    this);
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


        // If this setup is associated with a board ID, clear that association.
        if (svm.BoardId.HasValue)
        {
            await databaseService.PutAsync(new Board(svm.BoardId.Value, null));
        }

        // Notify associated calibrations and linkages about the deletion
        await databaseService.DeleteAsync<Setup>(vm.Id);
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

            var svm = setupViewModelFactory.Create(setup, newSetupsBoardId, false, this);
            svm.IsDirty = true;
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
