using Sufni.App.Shared.Models;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Coordinators;
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

    private readonly IShellCoordinator shell;
    private readonly IShellActions shellActions;
    private readonly IRecordedSessionProjection recordedSessionProjection;
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly ISessionVideoStore videoStore;
    private readonly ObservableCollectionExtended<SessionRowViewModel> sessionRowsSource = [];
    private readonly ReadOnlyObservableCollection<SessionRowViewModel> sessionRows;
    private readonly BehaviorSubject<Func<RecordedSessionSummary, bool>> filterSubject = new(_ => true);
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
        IPendingDeletes pendingDeletes,
        TimeProvider timeProvider,
        IShellCoordinator shell,
        IShellActions shellActions)
        : base(uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.recordedSessionProjection = recordedSessionProjection;
        this.sessionCoordinator = sessionCoordinator;
        this.shell = shell;
        this.shellActions = shellActions;
        this.videoStore = videoStore;
        sessionRows = new ReadOnlyObservableCollection<SessionRowViewModel>(sessionRowsSource);
        ObservePendingDeletes<PendingDeleteKey.Session>(pendingDeletes);
        MenuItems.Add(new("sync", shellActions.SyncCommand));
        MenuItems.Add(new("import", OpenImportCommand));
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
                    shell,
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

        filterSubject.OnNext(summary =>
        {
            if (IsPendingDelete(new PendingDeleteKey.Session(summary.Id))) return false;

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

    private bool CanOpenImport() => shellActions.CanImportSessions;

    [RelayCommand(CanExecute = nameof(CanOpenImport))]
    private Task OpenImport()
    {
        shell.OpenEditor(new EditorKey.ImportSessions());
        return Task.CompletedTask;
    }

    private void RequestRowDelete(SessionRowViewModel row) =>
        ScheduleDelete(new PendingDeleteKey.Session(row.Id), row.BaseName);

    private async Task RecalculateSessionAsync(SessionRowViewModel row)
    {
        var result = await sessionCoordinator.RequestRecomputeAsync(row.Id, RecomputeReason.ManualFromList);

        switch (result)
        {
            case SessionRecomputeResult.Recomputed:
                break;

            case SessionRecomputeResult.NotRecomputable:
                ErrorMessages.Add(new FailurePresentation("Session cannot be recalculated in its current state."));
                break;

            case SessionRecomputeResult.Missing:
                Notifications.Add("The session is no longer available.");
                break;
            case SessionRecomputeResult.ProcessingFailed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SessionRecomputeResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
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
    private System.Threading.Tasks.Task RowSelected(SessionRowViewModel? row)
    {
        if (row is null) return System.Threading.Tasks.Task.CompletedTask;
        shell.OpenEditor(new EditorKey.Session(row.Id));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    #endregion Commands

    private sealed record SessionListEntry(RecordedSessionSummary Summary, SessionVideoSnapshot? LatestVideo);
}
