using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.ViewModels.ItemLists;

/// <summary>
/// Shared search-bar / date-filter / menu-item state for the entity
/// list view models. After Slice 3 every concrete list owns its own
/// store-backed projection and exposes it via a `new` shadow on
/// <c>Items</c>; the base contributes only the cross-cutting filter
/// state and the menu-item collection.
/// </summary>
public partial class ItemListViewModelBase : ViewModelBase
{
    #region Observable properties

    [ObservableProperty] private string? searchText;
    [ObservableProperty] private bool searchBoxIsFocused;
    [ObservableProperty] private DateTime? dateFilterFrom;
    [ObservableProperty] private DateTime? dateFilterTo;
    [ObservableProperty] private bool dateFilterVisible;

    public ObservableCollection<PullMenuItemViewModel> MenuItems { get; set; } = [];

    #endregion Observable properties

    #region Property change handlers

    partial void OnSearchBoxIsFocusedChanged(bool value)
    {
        if (value)
        {
            DateFilterVisible = true;
        }
    }

    #endregion Property change handlers

    #region Virtual methods

    protected virtual void AddImplementation() { }

    #endregion Virtual methods

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
