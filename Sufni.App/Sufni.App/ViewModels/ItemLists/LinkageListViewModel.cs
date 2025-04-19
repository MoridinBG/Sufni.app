using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using Sufni.App.Models.Telemetry;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.ItemLists;

public partial class LinkageListViewModel : ItemListViewModelBase
{
    [ObservableProperty] private bool hasLinkages;

    public LinkageListViewModel()
    {
        Source.CountChanged.Subscribe(_ => { HasLinkages = Source.Count != 0; });
    }

    protected override async Task DeleteImplementation(ItemViewModelBase vm)
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");
        await databaseService!.DeleteLinkageAsync(vm.Id);
    }

    protected override void AddImplementation()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var linkage = new Linkage(Guid.NewGuid(), "new linkage", 65, 180, 65, "");
            var lvm = new LinkageViewModel(linkage, false)
            {
                IsDirty = true
            };

            OpenPage(lvm);
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not add Linkage: {e.Message}");
        }
    }

    private async Task LoadLinkages()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var linkagesList = await databaseService.GetLinkagesAsync();
            foreach (var linkage in linkagesList)
            {
                Source.AddOrUpdate(new LinkageViewModel(linkage, true));
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not load Linkages: {e.Message}");
        }
    }

    public override async Task LoadFromDatabase()
    {
        Source.Clear();
        await LoadLinkages();
    }
}
