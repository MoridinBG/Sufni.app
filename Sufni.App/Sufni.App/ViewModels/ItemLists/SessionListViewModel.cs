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

// Inherits from ItemListViewModelBase for the shared search-bar /
// date-filter / menu-item state. The items collection is owned locally
// — `sessionRows` is a typed projection from the store, exposed via
// the `new` shadow on `Items`.
public partial class SessionListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly ISessionStore sessionStore;
    private readonly SessionCoordinator sessionCoordinator;
    private readonly ReadOnlyObservableCollection<SessionRowViewModel> sessionRows;
    private readonly BehaviorSubject<Func<SessionSnapshot, bool>> filterSubject = new(_ => true);
    private (Guid Id, string Name)? pendingDelete;

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<SessionRowViewModel> Items => sessionRows;

    #endregion Observable properties

    #region Constructors

    public SessionListViewModel(ISessionStore sessionStore, SessionCoordinator sessionCoordinator)
    {
        this.sessionStore = sessionStore;
        this.sessionCoordinator = sessionCoordinator;

        sessionStore.Connect()
            .Filter(filterSubject)
            .TransformWithInlineUpdate(
                snapshot => new SessionRowViewModel(snapshot, sessionCoordinator, RequestRowDelete),
                (row, snapshot) => row.Update(snapshot))
            .SortAndBind(
                out sessionRows,
                SortExpressionComparer<SessionRowViewModel>.Descending(r => r.Timestamp ?? DateTime.MinValue))
            .Subscribe();

        // Push a fresh predicate to our filter subject whenever the
        // search text or date-filter bounds change.
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SearchText) or nameof(DateFilterFrom) or nameof(DateFilterTo))
            {
                RebuildFilter();
            }
        };
    }

    #endregion Constructors

    #region ItemListViewModelBase overrides

    protected override void RebuildFilter()
    {
        var search = SearchText;
        var fromDate = DateFilterFrom;
        var toDate = DateFilterTo;
        var pendingId = pendingDelete?.Id;

        filterSubject.OnNext(snapshot =>
        {
            if (pendingId is not null && snapshot.Id == pendingId) return false;

            // Search matches name OR description.
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

    protected override void OnPendingDeleteUndone()
    {
        pendingDelete = null;
        RebuildFilter();
    }

    #endregion ItemListViewModelBase overrides

    #region Private methods

    private void RequestRowDelete(SessionRowViewModel row)
    {
        _ = RunActionSwallowExceptionToErrorMessages(async () =>
        {
            var snapshot = sessionStore.Get(row.Id);
            if (snapshot is null) return;

            await FlushPendingDeleteAsync();

            pendingDelete = (snapshot.Id, snapshot.Name);
            RebuildFilter();

            StartUndoWindow(snapshot.Name, () => FinalizeSessionDeleteAsync(snapshot.Id));
        });
    }

    private async Task FinalizeSessionDeleteAsync(Guid sessionId)
    {
        pendingDelete = null;
        RebuildFilter();

        var result = await sessionCoordinator.DeleteAsync(sessionId);
        if (result.Outcome == SessionDeleteOutcome.Failed)
        {
            ErrorMessages.Add($"Session could not be deleted: {result.ErrorMessage}");
        }
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private async Task RowSelected(SessionRowViewModel? row)
    {
        if (row is null) return;
        await sessionCoordinator.OpenEditAsync(row.Id);
    }

    #endregion Commands
}
