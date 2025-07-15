using System;
using System.Diagnostics;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.ItemLists;

public partial class PairedDeviceListViewModel : ItemListViewModelBase
{
    #region Private methods

    private async Task LoadPairedDevicesAsync()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var pairedDeviceList = await databaseService.GetPairedDevicesAsync();
            foreach (var pairedDevice in pairedDeviceList)
            {
                var svm = new PairedDeviceViewModel(pairedDevice);
                Source.AddOrUpdate(svm);
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not load Paired devices: {e.Message}");
        }
    }

    #endregion Private methods
    
    #region ItemListViewModelBase overrides

    protected override async Task DeleteImplementation(ItemViewModelBase vm)
    {
        var pdvm = vm as PairedDeviceViewModel;
        Debug.Assert(pdvm is not null);
        Debug.Assert(pdvm.Name is not null);
        Debug.Assert(databaseService is not null);

        await databaseService.DeletePairedDeviceAsync(pdvm.Name);
    }

    public override async Task LoadFromDatabase()
    {
        Source.Clear();
        await LoadPairedDevicesAsync();
    }

    #endregion ItemListViewModelBase overrides
}