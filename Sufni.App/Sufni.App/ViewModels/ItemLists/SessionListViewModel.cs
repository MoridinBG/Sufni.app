using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using Sufni.App.Coordinators;
using Sufni.App.SessionGraph;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.ViewModels.ItemLists;

// Inherits from ItemListViewModelBase for the shared search-bar /
// date-filter / menu-item state. The items collection is owned locally
// — `sessionRows` is a typed projection from the store, exposed via
// the `new` shadow on `Items`.
public partial class SessionListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly SessionCoordinator sessionCoordinator;
    private readonly ReadOnlyObservableCollection<SessionRowViewModel> sessionRows;
    private readonly BehaviorSubject<Func<RecordedSessionSummary, bool>> filterSubject = new(_ => true);
    private readonly HashSet<Guid> pendingDeleteIds = [];

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<SessionRowViewModel> Items => sessionRows;

    #endregion Observable properties

    #region Constructors

    public SessionListViewModel(IRecordedSessionGraph recordedSessionGraph, SessionCoordinator sessionCoordinator)
    {
        this.sessionCoordinator = sessionCoordinator;

        recordedSessionGraph.ConnectSessions()
            .Filter(filterSubject)
            .TransformWithInlineUpdate(
                summary => new SessionRowViewModel(summary, sessionCoordinator, RequestRowDelete),
                (row, summary) => row.Update(summary))
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
        var pendingIds = pendingDeleteIds.Count == 0 ? null : new HashSet<Guid>(pendingDeleteIds);

        filterSubject.OnNext(summary =>
        {
            if (pendingIds is not null && pendingIds.Contains(summary.Id)) return false;

            // Search matches name OR description.
            var textMatch =
                string.IsNullOrEmpty(search) ||
                summary.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                summary.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase);
            if (!textMatch) return false;

            if (summary.Timestamp is null) return true;

            var ts = DateTimeOffset.FromUnixTimeSeconds(summary.Timestamp.Value).LocalDateTime;
            if (fromDate is not null && ts < fromDate) return false;
            if (toDate is not null && ts > toDate) return false;

            return true;
        });
    }

    #endregion ItemListViewModelBase overrides

    #region Private methods

    private void RequestRowDelete(SessionRowViewModel row)
    {
        pendingDeleteIds.Add(row.Id);
        RebuildFilter();

        StartUndoWindow(
            row.BaseName,
            finalize: () => FinalizeSessionDeleteAsync(row.Id),
            onUndone: () => OnSessionDeleteUndone(row.Id));
    }

    private void OnSessionDeleteUndone(Guid sessionId)
    {
        pendingDeleteIds.Remove(sessionId);
        RebuildFilter();
    }

    private async Task FinalizeSessionDeleteAsync(Guid sessionId)
    {
        var result = await sessionCoordinator.DeleteAsync(sessionId);

        pendingDeleteIds.Remove(sessionId);
        RebuildFilter();

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
