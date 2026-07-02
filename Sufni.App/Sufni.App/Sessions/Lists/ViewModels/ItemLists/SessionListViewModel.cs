using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Lists.ViewModels.Rows;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Shared.Base;
namespace Sufni.App.Sessions.Lists.ViewModels.ItemLists;

/// <summary>
/// List state for recorded sessions.
/// It maintains the searchable and date-filtered row collection, tracks
/// pending delete undo windows, and reflects stale/no-raw session status in
/// the rows.
/// </summary>
public partial class SessionListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly ISessionCoordinator sessionCoordinator;
    private readonly IRecordedSessionListExtensionService? listExtensionService;
    private readonly ReadOnlyObservableCollection<SessionRowViewModel> sessionRows;
    private readonly BehaviorSubject<Func<RecordedSessionSummary, bool>> filterSubject = new(_ => true);
    private readonly HashSet<Guid> pendingDeleteIds = [];
    private readonly Dictionary<SessionDateGroupKey, bool> dateGroupExpansionState = [];

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<SessionRowViewModel> Items => sessionRows;

    public ObservableCollection<SessionDateGroupViewModel> DateGroups { get; } = [];

    #endregion Observable properties

    #region Constructors

    public SessionListViewModel(
        IRecordedSessionProjection recordedSessionProjection,
        ISessionCoordinator sessionCoordinator,
        IUiThreadDispatcher uiThreadDispatcher,
        IRecordedSessionListExtensionService? listExtensionService = null,
        IBackgroundTaskRunner? backgroundTaskRunner = null)
        : base(uiThreadDispatcher, backgroundTaskRunner)
    {
        this.sessionCoordinator = sessionCoordinator;
        this.listExtensionService = listExtensionService;
        if (this.listExtensionService is not null)
        {
            this.listExtensionService.ContributionsChanged += OnListExtensionContributionsChanged;
        }

        recordedSessionProjection.ConnectSessions()
            .Filter(filterSubject)
            .TransformWithInlineUpdate(
                summary => new SessionRowViewModel(
                    summary,
                    sessionCoordinator,
                    RequestRowDelete,
                    RecalculateSessionAsync,
                    this.listExtensionService),
                (row, summary) => row.Update(summary))
            .SortAndBind(
                out sessionRows,
                SortExpressionComparer<SessionRowViewModel>.Descending(r => r.Timestamp ?? DateTime.MinValue))
            .Subscribe();

        ((INotifyCollectionChanged)sessionRows).CollectionChanged += OnSessionRowsChanged;
        SynchronizeDateGroups();

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
        var toDateExclusive = DateFilterTo?.Date.AddDays(1);
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
            if (toDateExclusive is not null && ts >= toDateExclusive) return false;

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

    private async Task RecalculateSessionAsync(SessionRowViewModel row)
    {
        var result = await sessionCoordinator.RequestRecomputeAsync(row.Id, RecomputeReason.ManualFromList);

        switch (result)
        {
            case SessionRecomputeResult.Recomputed:
                break;

            case SessionRecomputeResult.NotRecomputable:
                ErrorMessages.Add("Session cannot be recalculated in its current state.");
                break;

            case SessionRecomputeResult.Failed failed:
                ErrorMessages.Add($"Session could not be recalculated: {failed.ErrorMessage}");
                break;
        }
    }

    private void OnSessionRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SynchronizeDateGroups();
    }

    private void OnListExtensionContributionsChanged(object? sender, EventArgs e)
    {
        if (UiThreadDispatcher.CheckAccess())
        {
            RefreshSessionRowExtensionContributions();
        }
        else
        {
            UiThreadDispatcher.Post(RefreshSessionRowExtensionContributions);
        }
    }

    private void RefreshSessionRowExtensionContributions()
    {
        foreach (var row in sessionRows)
        {
            row.RefreshExtensionContributions();
        }
    }

    private void SynchronizeDateGroups()
    {
        foreach (var group in DateGroups)
        {
            dateGroupExpansionState[group.Key] = group.IsExpanded;
        }

        var groups = sessionRows
            .GroupBy(row => row.DateGroupKey)
            .OrderByDescending(group => group.Key.Date ?? DateOnly.MinValue)
            .Select(group => (Key: group.Key, Rows: group.ToList()))
            .ToList();
        var desiredKeys = groups.Select(group => group.Key).ToHashSet();

        for (var index = DateGroups.Count - 1; index >= 0; index--)
        {
            if (!desiredKeys.Contains(DateGroups[index].Key))
            {
                DateGroups.RemoveAt(index);
            }
        }

        for (var desiredIndex = 0; desiredIndex < groups.Count; desiredIndex++)
        {
            var group = groups[desiredIndex];
            var existingIndex = IndexOfDateGroup(group.Key);
            SessionDateGroupViewModel groupViewModel;

            if (existingIndex >= 0)
            {
                groupViewModel = DateGroups[existingIndex];
                if (existingIndex != desiredIndex)
                {
                    DateGroups.Move(existingIndex, desiredIndex);
                }
            }
            else
            {
                var isExpanded = !dateGroupExpansionState.TryGetValue(group.Key, out var storedExpanded) || storedExpanded;
                groupViewModel = new SessionDateGroupViewModel(group.Key, isExpanded);
                DateGroups.Insert(desiredIndex, groupViewModel);
            }

            groupViewModel.SetRows(group.Rows);
        }
    }

    private int IndexOfDateGroup(SessionDateGroupKey key)
    {
        for (var index = 0; index < DateGroups.Count; index++)
        {
            if (DateGroups[index].Key == key)
            {
                return index;
            }
        }

        return -1;
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
