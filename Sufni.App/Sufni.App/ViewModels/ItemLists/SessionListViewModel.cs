using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using Sufni.App.Coordinators;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.ViewModels.ItemLists;

// Inherits from ItemListViewModelBase so the shared SearchBar /
// UndoDeleteButton / PullableMenuScrollViewer keep resolving against the
// base type. The base SourceCache<ItemViewModelBase> is unused — the
// new flow projects ISessionStore.Connect() into a separate
// `sessionRows` collection that shadows the base Items property.
public partial class SessionListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly ISessionCoordinator sessionCoordinator;
    private readonly ReadOnlyObservableCollection<SessionRowViewModel> sessionRows;
    private readonly BehaviorSubject<Func<SessionSnapshot, bool>> filterSubject = new(_ => true);

    #endregion Private fields

    #region Observable properties

    public new ReadOnlyObservableCollection<SessionRowViewModel> Items => sessionRows;

    #endregion Observable properties

    #region Constructors

    public SessionListViewModel()
    {
        sessionCoordinator = null!;
        sessionRows = new ReadOnlyObservableCollection<SessionRowViewModel>([]);
    }

    public SessionListViewModel(ISessionStore sessionStore, ISessionCoordinator sessionCoordinator)
    {
        this.sessionCoordinator = sessionCoordinator;

        sessionStore.Connect()
            .Filter(filterSubject)
            .TransformWithInlineUpdate(
                snapshot => new SessionRowViewModel(snapshot, sessionCoordinator),
                (row, snapshot) => row.Update(snapshot))
            .SortAndBind(
                out sessionRows,
                SortExpressionComparer<SessionRowViewModel>.Descending(r => r.Timestamp ?? DateTime.MinValue))
            .Subscribe();

        // The base's OnSearchTextChanged / OnDateFilterFromChanged /
        // OnDateFilterToChanged partial methods only refresh the (empty)
        // base SourceCache. We need to push a fresh predicate to our own
        // filter subject when any of those change.
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SearchText) or nameof(DateFilterFrom) or nameof(DateFilterTo))
            {
                RebuildFilter();
            }
        };
    }

    #endregion Constructors

    #region Private methods

    private void RebuildFilter()
    {
        var search = SearchText;
        var fromDate = DateFilterFrom;
        var toDate = DateFilterTo;

        filterSubject.OnNext(snapshot =>
        {
            // Search matches name OR description (legacy ConnectSource
            // checked both).
            var textMatch =
                string.IsNullOrEmpty(search) ||
                snapshot.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                snapshot.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase);
            if (!textMatch) return false;

            if (snapshot.Timestamp is null) return true;

            var ts = DateTimeOffset.FromUnixTimeSeconds(snapshot.Timestamp.Value).LocalDateTime;
            if (fromDate is not null && ts < fromDate) return false;
            if (toDate is not null && ts > toDate) return false;

            return true;
        });
    }

    #endregion Private methods

    #region ItemListViewModelBase overrides

    public override Task LoadFromDatabase() => Task.CompletedTask;

    #endregion ItemListViewModelBase overrides

    #region Commands

    [RelayCommand]
    private async Task RowSelected(SessionRowViewModel? row)
    {
        if (row is null) return;
        await sessionCoordinator.OpenEditAsync(row.Id);
    }

    #endregion Commands
}
