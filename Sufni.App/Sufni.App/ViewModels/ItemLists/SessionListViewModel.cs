using System;
using System.Diagnostics;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.ItemLists;

public partial class SessionListViewModel : ItemListViewModelBase
{
    #region Private methods

    private async Task LoadSessionsAsync()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var sessionList = await databaseService.GetSessionsAsync();
            foreach (var session in sessionList)
            {
                Source.AddOrUpdate(new SessionViewModel(session, true));
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not load Sessions: {e.Message}");
        }
    }

    #endregion Private methods

    #region ItemListViewModelBase overrides

    public override void ConnectSource()
    {
        Source.Connect()
            .Filter(vm => string.IsNullOrEmpty(SearchText) ||
                           (vm.Name is not null &&
                            vm.Name!.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)) ||
                           (vm is SessionViewModel svm &&
                            svm.Description.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)))
            .Filter(svm => (DateFilterFrom is null || svm.Timestamp >= DateFilterFrom) &&
                           (DateFilterTo is null || svm.Timestamp <= DateFilterTo))
            .SortAndBind(out items, SortExpressionComparer<ItemViewModelBase>.Descending(svm => svm.Timestamp!))
            .DisposeMany()
            .Subscribe();
    }

    public override async Task LoadFromDatabase()
    {
        Source.Clear();
        await LoadSessionsAsync();
    }

    #endregion ItemListViewModelBase overrides
}
