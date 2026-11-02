using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.Telemetry;
using Sufni.App.Bikes.Models;

using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Stores;
using System.Reactive;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Bikes.Services;
namespace Sufni.App.LiveDaq.Queries;

// Keeps a cached, query-shaped view of known live DAQ boards so the coordinator can
// seed offline rows and refresh setup or bike labels cheaply.
internal sealed class LiveDaqKnownBoardsQuery : ILiveDaqKnownBoardsQuery, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqKnownBoardsQuery>();

    private sealed record KnownLiveDaqProjection(
        KnownLiveDaqRecord Record,
        LiveDaqTravelCalibration? TravelCalibration,
        LiveDaqSessionContext? SessionContext);

    private readonly IBoardStore boardStore;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly IUiThreadDispatcher dispatcher;
    private readonly ISetupStore setupStore;
    private readonly IBikeStore bikeStore;
    private readonly ITelemetryBikeProcessingContextFactory bikeProcessingContextFactory;
    private readonly BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>> changesSubject = new([]);
    private IReadOnlyDictionary<string, KnownLiveDaqProjection> currentProjections = new Dictionary<string, KnownLiveDaqProjection>(StringComparer.OrdinalIgnoreCase);
    private readonly IDisposable subscription;
    private bool disposed;

    public LiveDaqKnownBoardsQuery(
        IBoardStore boardStore,
        ISetupStore setupStore,
        IBikeStore bikeStore,
        ITelemetryBikeProcessingContextFactory bikeProcessingContextFactory,
        IBackgroundTaskRunner backgroundTaskRunner,
        IUiThreadDispatcher dispatcher,
        StorePublication publication)
    {
        this.boardStore = boardStore;
        this.setupStore = setupStore;
        this.bikeStore = bikeStore;
        this.bikeProcessingContextFactory = bikeProcessingContextFactory;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.dispatcher = dispatcher;
        subscription = publication.Committed
            .Where(commit => commit.Stores.Contains(boardStore) || commit.Stores.Contains(setupStore) || commit.Stores.Contains(bikeStore))
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default)
            .Select(_ => CaptureInputs())
            .Select(inputs => Observable.FromAsync(cancellationToken => RefreshAsync(inputs, cancellationToken)))
            .Switch()
            .Subscribe();
    }

    public IObservable<IReadOnlyList<KnownLiveDaqRecord>> Changes => changesSubject;

    public KnownLiveDaqRecord? Get(string identityKey) =>
        currentProjections.TryGetValue(identityKey, out var projection) ? projection.Record : null;

    public LiveDaqTravelCalibration? GetTravelCalibration(string identityKey)
    {
        return currentProjections.TryGetValue(identityKey, out var projection)
            ? projection.TravelCalibration
            : null;
    }

    public LiveDaqSessionContext? GetSessionContext(string identityKey)
    {
        return currentProjections.TryGetValue(identityKey, out var projection)
            ? projection.SessionContext
            : null;
    }

    private static LiveDaqTravelCalibration CreateTravelCalibration(BikeData bikeData)
    {
        var front = CreateCalibration(bikeData.FrontMaxTravel, bikeData.FrontMeasurementToTravel);
        var rear = CreateCalibration(bikeData.RearMaxTravel, bikeData.RearMeasurementToTravel);

        return new LiveDaqTravelCalibration(front, rear);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        subscription.Dispose();
        changesSubject.Dispose();
    }

    private BoardInput[] CaptureInputs() => boardStore.GetAll().Select(board =>
    {
        var setup = board.SetupId is { } setupId ? setupStore.Get(setupId) : null;
        return new BoardInput(board, setup, setup is null ? null : bikeStore.Get(setup.BikeId));
    }).ToArray();

    private async Task RefreshAsync(BoardInput[] inputs, CancellationToken cancellationToken)
    {
        try
        {
            var projections = await backgroundTaskRunner.RunAsync(() => inputs.OrderBy(input => input.Board.Id)
                .Select(BuildProjection).ToDictionary(projection => projection.Record.IdentityKey, StringComparer.OrdinalIgnoreCase), cancellationToken)
                .ConfigureAwait(false);
            var records = projections.Values.Select(projection => projection.Record).ToArray();
            await dispatcher.InvokeAsync(() =>
            {
                if (disposed || cancellationToken.IsCancellationRequested) return;
                currentProjections = projections;
                changesSubject.OnNext(records);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.Warning(exception, "Building known live DAQ boards failed; keeping the last snapshot");
        }
    }

    private sealed record BoardInput(BoardSnapshot Board, SetupSnapshot? Setup, BikeSnapshot? Bike);

    private KnownLiveDaqProjection BuildProjection(BoardInput input)
    {
        var (board, setupSnapshot, bikeSnapshot) = input;
        var boardId = board.Id.ToString();
        var record = new KnownLiveDaqRecord(
            IdentityKey: boardId,
            DisplayName: setupSnapshot?.Name ?? boardId,
            BoardId: boardId,
            SetupId: setupSnapshot?.Id,
            SetupName: setupSnapshot?.Name,
            BikeId: bikeSnapshot?.Id,
            BikeName: bikeSnapshot?.Name);

        if (setupSnapshot is null || bikeSnapshot is null)
        {
            return new KnownLiveDaqProjection(record, null, null);
        }

        var bikeProcessingContext = bikeProcessingContextFactory.Create(setupSnapshot, bikeSnapshot);
        var calibration = CreateTravelCalibration(bikeProcessingContext.BikeData);

        return new KnownLiveDaqProjection(
            Record: record,
            TravelCalibration: calibration is { Front: null, Rear: null } ? null : calibration,
            SessionContext: new LiveDaqSessionContext(
                IdentityKey: record.IdentityKey,
                BoardId: Guid.Parse(record.BoardId),
                DisplayName: record.DisplayName,
                SetupId: record.SetupId!.Value,
                SetupName: record.SetupName!,
                BikeId: record.BikeId!.Value,
                BikeName: record.BikeName!,
                BikeData: bikeProcessingContext.BikeData,
                TravelCalibration: calibration,
                DampingSpeedCutoffs: bikeSnapshot.DampingSpeedCutoffs));
    }

    private static LiveDaqTravelChannelCalibration? CreateCalibration(
        double? maxTravel,
        Func<ushort, double>? measurementToTravel)
    {
        if (maxTravel is not > 0 || measurementToTravel is null)
        {
            return null;
        }

        return new LiveDaqTravelChannelCalibration(maxTravel.Value, measurementToTravel);
    }
}
