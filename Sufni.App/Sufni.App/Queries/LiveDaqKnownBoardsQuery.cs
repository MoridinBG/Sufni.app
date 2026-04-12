using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Queries;

// Keeps a cached, query-shaped view of known live DAQ boards so the coordinator can
// seed offline rows and refresh setup or bike labels cheaply.
public sealed class LiveDaqKnownBoardsQuery : ILiveDaqKnownBoardsQuery, IDisposable
{
    private readonly IDatabaseService databaseService;
    private readonly ISetupStore setupStore;
    private readonly IBikeStore bikeStore;
    private readonly BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>> changesSubject = new([]);
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly IDisposable setupSubscription;
    private readonly IDisposable bikeSubscription;

    public LiveDaqKnownBoardsQuery(
        IDatabaseService databaseService,
        ISetupStore setupStore,
        IBikeStore bikeStore)
    {
        this.databaseService = databaseService;
        this.setupStore = setupStore;
        this.bikeStore = bikeStore;

        setupSubscription = setupStore.Connect().Subscribe(changes =>
        {
            _ = RefreshAsync();
        });
        bikeSubscription = bikeStore.Connect().Subscribe(changes =>
        {
            _ = RefreshAsync();
        });

        _ = RefreshAsync();
    }

    public IObservable<IReadOnlyList<KnownLiveDaqRecord>> Changes => changesSubject;

    public void Dispose()
    {
        setupSubscription.Dispose();
        bikeSubscription.Dispose();
        refreshGate.Dispose();
        changesSubject.Dispose();
    }

    private async Task RefreshAsync()
    {
        await refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var boards = await databaseService.GetAllAsync<Board>().ConfigureAwait(false);

            var records = boards
                .OrderBy(board => board.Id)
                .Select(BuildRecord)
                .ToArray();

            changesSubject.OnNext(records);
        }
        catch
        {
            // No query-level error surface exists yet. Leave the last
            // successful snapshot intact and retry on the next change.
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private KnownLiveDaqRecord BuildRecord(Board board)
    {
        var setup = board.SetupId.HasValue
            ? setupStore.Get(board.SetupId.Value)
            : null;

        setup ??= setupStore.FindByBoardId(board.Id);

        var bike = setup is null
            ? null
            : bikeStore.Get(setup.BikeId);

        var boardId = board.Id.ToString();
        return new KnownLiveDaqRecord(
            IdentityKey: boardId,
            DisplayName: boardId,
            BoardId: boardId,
            SetupId: setup?.Id,
            SetupName: setup?.Name,
            BikeId: bike?.Id,
            BikeName: bike?.Name);
    }
}