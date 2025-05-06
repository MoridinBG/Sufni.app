using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using Sufni.App.Models;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.ItemLists;

public partial class BikeListViewModel : ItemListViewModelBase
{
    [ObservableProperty] private bool hasBikes;

    public BikeListViewModel()
    {
        Source.CountChanged.Subscribe(_ => { HasBikes = Source.Count != 0; });
    }

    protected override async Task DeleteImplementation(ItemViewModelBase vm)
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");
        await databaseService!.DeleteBikeAsync(vm.Id);
    }

    protected override void AddImplementation()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var bike = new Bike(Guid.NewGuid(), "new bike");
            var bvm = new BikeViewModel(bike, false)
            {
                IsDirty = true
            };

            OpenPage(bvm);
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not add Linkage: {e.Message}");
        }
    }

    private async Task LoadBikesAsync()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var bikeList = await databaseService.GetBikesAsync();
            foreach (var bike in bikeList)
            {
                Source.AddOrUpdate(new BikeViewModel(bike, true));
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not load Bike: {e.Message}");
        }
    }

    public override async Task LoadFromDatabase()
    {
        Source.Clear();
        await LoadBikesAsync();
    }
}
