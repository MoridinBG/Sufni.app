using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Services;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.ItemLists;

public partial class ItemListViewModelBase : ViewModelBase
{
    protected readonly IDatabaseService? databaseService;

    #region Observable properties

    [ObservableProperty] private string? searchText;
    [ObservableProperty] private bool searchBoxIsFocused;
    [ObservableProperty] private DateTime? dateFilterFrom;
    [ObservableProperty] private DateTime? dateFilterTo;
    [ObservableProperty] private bool dateFilterVisible;
    [ObservableProperty] private ItemViewModelBase? lastDeleted;

    public readonly SourceCache<ItemViewModelBase, Guid> Source = new(x => x.Id);
    public ReadOnlyObservableCollection<ItemViewModelBase> Items => items;
    protected ReadOnlyObservableCollection<ItemViewModelBase> items;
    public ObservableCollection<PullMenuItemViewModel> MenuItems { get; set; } = [];

    #endregion Observable properties

    #region Property change handlers

    partial void OnSearchTextChanged(string? value)
    {
        Source.Refresh();
    }

    public void OnAdded(ItemViewModelBase vm)
    {
        Source.AddOrUpdate(vm);
    }

    partial void OnDateFilterFromChanged(DateTime? value)
    {
        Source.Refresh();
    }

    partial void OnDateFilterToChanged(DateTime? value)
    {
        Source.Refresh();
    }

    partial void OnSearchBoxIsFocusedChanged(bool value)
    {
        if (value)
        {
            DateFilterVisible = true;
        }
    }
    
    #endregion Property change handlers
    
    #region Virtual methods

    public virtual Task LoadFromDatabase() { return Task.CompletedTask; }
    public virtual void ConnectSource()
    {
        Source.Connect()
            .Filter(vm => string.IsNullOrEmpty(SearchText) ||
                            (vm.Name is not null && vm.Name.Contains(SearchText,
                                StringComparison.CurrentCultureIgnoreCase)))
            .Bind(out items)
            .DisposeMany()
            .Subscribe();
    }

    protected virtual void AddImplementation() { }
    protected virtual Task DeleteImplementation(ItemViewModelBase vm) { return Task.CompletedTask; }

    #endregion Virtual methods

    #region Constructors

#pragma warning disable CS8618 // "items" is populated in the ConnectSource method
    public ItemListViewModelBase()
    {
        databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        ConnectSource();
    }
#pragma warning restore CS8618

    #endregion Constructors

    #region Public methods

    public async Task Delete(ItemViewModelBase vm)
    {
        Source.Remove(vm);
        await DeleteImplementation(vm);
    }

    public void UndoableDelete(ItemViewModelBase vm)
    {
        LastDeleted = vm;
        Source.Remove(vm);
    }

    #endregion Public methods

    #region Commands

    [RelayCommand]
    private void Add()
    {
        AddImplementation();
    }

    [RelayCommand]
    private void ClearSearchText()
    {
        SearchText = null;
        DateFilterVisible = false;
    }

    [RelayCommand]
    public async Task FinalizeDelete()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");
        Debug.Assert(LastDeleted != null, nameof(LastDeleted) + " != null");

        switch (LastDeleted)
        {
            case SetupViewModel:
                await databaseService.DeleteSetupAsync(LastDeleted.Id);
                break;
            case SessionViewModel:
                await databaseService.DeleteSessionAsync(LastDeleted.Id);
                break;
            case BikeViewModel:
                await databaseService.DeleteBikeAsync(LastDeleted.Id);
                break;
        }
        LastDeleted = null;
    }

    [RelayCommand]
    public void UndoDelete()
    {
        Debug.Assert(LastDeleted != null, nameof(LastDeleted) + " != null");

        Source.AddOrUpdate(LastDeleted);
        LastDeleted = null;
    }

    [RelayCommand]
    private void ClearDateFilter(string which)
    {
        switch (which)
        {
            case "from":
                DateFilterFrom = null;
                break;
            case "to":
                DateFilterTo = null;
                break;
        }
    }

    [RelayCommand]
    private void ToggleDateFilter()
    {
        DateFilterVisible = !DateFilterVisible;
    }

    #endregion Commands
}
