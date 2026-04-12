using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Coordinators;

// Reconciles discovery entries with known-board app data and routes row selection to
// identity-keyed live preview tabs.
public sealed class LiveDaqCoordinator : ILiveDaqCoordinator
{
    private readonly ILiveDaqStoreWriter liveDaqStore;
    private readonly ILiveDaqKnownBoardsQuery knownBoardsQuery;
    private readonly ILiveDaqCatalogService liveDaqCatalogService;
    private readonly ILiveDaqClientFactory liveDaqClientFactory;
    private readonly IShellCoordinator shell;
    private readonly IDialogService dialogService;

    private IReadOnlyDictionary<string, KnownLiveDaqRecord> knownBoards = new Dictionary<string, KnownLiveDaqRecord>();
    private IReadOnlyList<LiveDaqCatalogEntry> catalogEntries = [];
    private CompositeDisposable? activeSubscriptions;

    public LiveDaqCoordinator(
        ILiveDaqStoreWriter liveDaqStore,
        ILiveDaqKnownBoardsQuery knownBoardsQuery,
        ILiveDaqCatalogService liveDaqCatalogService,
        ILiveDaqClientFactory liveDaqClientFactory,
        IShellCoordinator shell,
        IDialogService dialogService)
    {
        this.liveDaqStore = liveDaqStore;
        this.knownBoardsQuery = knownBoardsQuery;
        this.liveDaqCatalogService = liveDaqCatalogService;
        this.liveDaqClientFactory = liveDaqClientFactory;
        this.shell = shell;
        this.dialogService = dialogService;
    }

    public void Activate()
    {
        if (activeSubscriptions is not null)
        {
            return;
        }

        var subscriptions = new CompositeDisposable();
        activeSubscriptions = subscriptions;

        subscriptions.Add(liveDaqCatalogService.AcquireBrowse());
        subscriptions.Add(knownBoardsQuery.Changes.Subscribe(records =>
        {
            knownBoards = records.ToDictionary(r => r.IdentityKey, StringComparer.OrdinalIgnoreCase);
            Reconcile();
        }));
        subscriptions.Add(liveDaqCatalogService.Observe().Subscribe(entries =>
        {
            catalogEntries = entries;
            Reconcile();
        }));
    }

    public void Deactivate()
    {
        activeSubscriptions?.Dispose();
        activeSubscriptions = null;

        catalogEntries = [];
        Reconcile();
    }

    public Task SelectAsync(string identityKey)
    {
        var snapshot = liveDaqStore.Get(identityKey);
        if (snapshot is null)
        {
            return Task.CompletedTask;
        }

        shell.OpenOrFocus<LiveDaqDetailViewModel>(
            detail => detail.IdentityKey == snapshot.IdentityKey,
            () => new LiveDaqDetailViewModel(snapshot, liveDaqClientFactory, shell, dialogService));

        return Task.CompletedTask;
    }

    // Reconciles the live DAQ snapshot state by merging known boards with current catalog entries.
    private void Reconcile()
    {
        var snapshots = new Dictionary<string, LiveDaqSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var knownBoard in knownBoards.Values)
        {
            snapshots[knownBoard.IdentityKey] = new LiveDaqSnapshot(
                IdentityKey: knownBoard.IdentityKey,
                DisplayName: knownBoard.DisplayName,
                BoardId: knownBoard.BoardId,
                Host: null,
                Port: null,
                IsOnline: false,
                SetupName: knownBoard.SetupName,
                BikeName: knownBoard.BikeName);
        }

        foreach (var entry in catalogEntries)
        {
            var merged = knownBoards.TryGetValue(entry.IdentityKey, out var knownBoard)
                ? new LiveDaqSnapshot(
                    IdentityKey: entry.IdentityKey,
                    DisplayName: knownBoard.DisplayName,
                    BoardId: entry.BoardId,
                    Host: entry.Host,
                    Port: entry.Port,
                    IsOnline: true,
                    SetupName: knownBoard.SetupName,
                    BikeName: knownBoard.BikeName)
                : new LiveDaqSnapshot(
                    IdentityKey: entry.IdentityKey,
                    DisplayName: entry.DisplayName,
                    BoardId: entry.BoardId,
                    Host: entry.Host,
                    Port: entry.Port,
                    IsOnline: true,
                    SetupName: null,
                    BikeName: null);

            snapshots[entry.IdentityKey] = merged;
        }

        liveDaqStore.Clear();
        foreach (var snapshot in snapshots.Values.OrderBy(snapshot => snapshot.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            liveDaqStore.Upsert(snapshot);
        }
    }
}