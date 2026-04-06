using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels.Factories;
using Sufni.App.ViewModels.Hosts;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.ItemLists;

public partial class BikeListViewModel : ItemListViewModelBase, IBikeSelectionSource, IBikeViewModelHost, IBikeCreator
{
    #region IBikeCreator

    public void AddBike() => AddCommand.Execute(null);

    #endregion IBikeCreator

    #region Private fields

    private readonly IBikeViewModelFactory bikeViewModelFactory;
    private readonly IBikeUsageQuery bikeUsageQuery;
    private readonly ReadOnlyObservableCollection<ItemViewModelBase> unfilteredBikes;

    #endregion Private fields

    #region Host callbacks

    public bool CanDeleteBike(Guid bikeId) => !bikeUsageQuery.IsBikeInUse(bikeId);

    public void OnBikeSaved(BikeViewModel vm) => OnAdded(vm);

    #endregion Host callbacks

    #region Observable properties

    [ObservableProperty] private bool hasBikes;

    #endregion Observable properties

    #region Constructors

    public BikeListViewModel()
    {
        bikeViewModelFactory = null!;
        bikeUsageQuery = null!;
        Source.Connect()
            .Bind(out unfilteredBikes)
            .Subscribe();
        Source.CountChanged.Subscribe(_ => { HasBikes = Source.Count != 0; });
    }

    public BikeListViewModel(
        IDatabaseService databaseService,
        IBikeViewModelFactory bikeViewModelFactory,
        INavigator navigator,
        IBikeUsageQuery bikeUsageQuery) : base(databaseService, navigator)
    {
        this.bikeViewModelFactory = bikeViewModelFactory;
        this.bikeUsageQuery = bikeUsageQuery;
        Source.Connect()
            .Bind(out unfilteredBikes)
            .Subscribe();
        Source.CountChanged.Subscribe(_ => { HasBikes = Source.Count != 0; });
    }

    #endregion Constructors

    #region Private methods

    private async Task LoadBikesAsync()
    {


        try
        {
            var bikeList = await databaseService.GetAllAsync<Bike>();
            foreach (var bike in bikeList)
            {
                Source.AddOrUpdate(bikeViewModelFactory.Create(bike, true, this));
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not load Bike: {e.Message}");
        }
    }

    #endregion Private methods
    
    #region ItemListViewModelBase overrides

    protected override async Task DeleteImplementation(ItemViewModelBase vm)
    {

        await databaseService.DeleteAsync<Bike>(vm.Id);
    }

    protected override void AddImplementation()
    {
        try
        {
            var bike = new Bike(Guid.NewGuid(), "new bike");
            var bvm = bikeViewModelFactory.Create(bike, false, this);
            bvm.IsDirty = true;

            OpenPage(bvm);
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not add Linkage: {e.Message}");
        }
    }

    public override async Task LoadFromDatabase()
    {
        Source.Clear();
        await LoadBikesAsync();
    }

    #endregion ItemListViewModelBase overrides

    #region IBikeSelectionSource

    public ReadOnlyObservableCollection<ItemViewModelBase> Bikes => unfilteredBikes;

    #endregion IBikeSelectionSource
}
