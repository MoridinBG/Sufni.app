using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using Sufni.App.Infrastructure;

using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Lists.ViewModels.Rows;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Shared.Base;
using Sufni.App.Video.Stores;
namespace Sufni.App.Sessions.Lists.ViewModels.ItemLists;

/// <summary>
/// List state for recorded sessions.
/// It maintains the searchable and date-filtered row collection, tracks
/// pending delete undo windows, and reflects stale/no-raw session status and
/// the linked video in the rows.
/// </summary>
public partial class SessionListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly IRecordedSessionProjection recordedSessionProjection;
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly ISessionVideoStore videoStore;
    private readonly ObservableCollectionExtended<SessionRowViewModel> sessionRowsSource = [];
    private readonly ReadOnlyObservableCollection<SessionRowViewModel> sessionRows;
    private readonly BehaviorSubject<Func<RecordedSessionSummary, bool>> filterSubject = new(_ => true);
    private readonly HashSet<Guid> pendingDeleteIds = [];
    private readonly Dictionary<SessionDateGroupKey, bool> dateGroupExpansionState = [];
    private bool subscriptionsAttached;
    private bool dateGroupSyncQueued;

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<SessionRowViewModel> Items => sessionRows;

    public ObservableCollection<SessionDateGroupViewModel> DateGroups { get; } = [];

    #endregion Observable properties

    #region Constructors

    public SessionListViewModel(
        IRecordedSessionProjection recordedSessionProjection,
        ISessionCoordinator sessionCoordinator,
        ISessionVideoStore videoStore,
        IUiThreadDispatcher uiThreadDispatcher,
        IBackgroundTaskRunner? backgroundTaskRunner = null)
        : base(uiThreadDispatcher, backgroundTaskRunner)
    {
        this.recordedSessionProjection = recordedSessionProjection;
        this.sessionCoordinator = sessionCoordinator;
        this.videoStore = videoStore;
        sessionRows = new ReadOnlyObservableCollection<SessionRowViewModel>(sessionRowsSource);
    }

    #endregion Constructors

    #region ItemListViewModelBase overrides

    protected override void AttachSubscriptions(CompositeDisposable subscriptions)
    {
        sessionRowsSource.Clear();
        RebuildFilter();
        subscriptionsAttached = true;

        subscriptions.Add(recordedSessionProjection.ConnectSessions()
            .Filter(filterSubject)
            .LeftJoinMany(
                videoStore.Connect(),
                video => video.SessionId,
                (summary, videos) => new SessionListEntry(summary, videos.Items.MaxBy(video => video.Added)))
            .TransformWithInlineUpdate(
                entry => new SessionRowViewModel(
                    entry.Summary,
                    entry.LatestVideo,
                    sessionCoordinator,
                    RequestRowDelete,
                    RecalculateSessionAsync),
                (row, entry) => row.Update(entry.Summary, entry.LatestVideo))
            .SortAndBind(
                sessionRowsSource,
                SortExpressionComparer<SessionRowViewModel>.Descending(r => r.Timestamp ?? DateTime.MinValue))
            .Subscribe());

        ((INotifyCollectionChanged)sessionRowsSource).CollectionChanged += OnSessionRowsChanged;
        subscriptions.Add(Disposable.Create(() =>
            ((INotifyCollectionChanged)sessionRowsSource).CollectionChanged -= OnSessionRowsChanged));

        PropertyChanged += OnListPropertyChanged;
        subscriptions.Add(Disposable.Create(() => PropertyChanged -= OnListPropertyChanged));

        SynchronizeDateGroups();
    }

    protected override void OnSubscriptionsDetached()
    {
        subscriptionsAttached = false;
        sessionRowsSource.Clear();
        SynchronizeDateGroups();
    }

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

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SearchText) or nameof(DateFilterFrom) or nameof(DateFilterTo))
        {
            RebuildFilter();
        }
    }

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
        if (dateGroupSyncQueued)
        {
            return;
        }

        dateGroupSyncQueued = true;
        UiThreadDispatcher.Post(() =>
        {
            dateGroupSyncQueued = false;
            if (subscriptionsAttached)
            {
                SynchronizeDateGroups();
            }
        });
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

    private sealed record SessionListEntry(RecordedSessionSummary Summary, SessionVideoSnapshot? LatestVideo);
}
