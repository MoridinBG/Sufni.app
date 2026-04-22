using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Services;
using Sufni.App.Stores;
using Serilog;

namespace Sufni.App.Queries;

// Keeps a cached, query-shaped view of known live DAQ boards so the coordinator can
// seed offline rows and refresh setup or bike labels cheaply.
public sealed class LiveDaqKnownBoardsQuery : ILiveDaqKnownBoardsQuery, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqKnownBoardsQuery>();

    private sealed record KnownLiveDaqProjection(
        KnownLiveDaqRecord Record,
        LiveDaqTravelCalibration? TravelCalibration,
        LiveDaqSessionContext? SessionContext);

    private readonly IDatabaseService databaseService;
    private readonly ISetupStore setupStore;
    private readonly IBikeStore bikeStore;
    private readonly BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>> changesSubject = new([]);
    private IReadOnlyDictionary<string, KnownLiveDaqProjection> currentProjections = new Dictionary<string, KnownLiveDaqProjection>(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly IDisposable setupSubscription;
    private readonly IDisposable bikeSubscription;
    private readonly object coalesceGate = new();
    private bool refreshInProgress;
    private bool refreshPending;

    public LiveDaqKnownBoardsQuery(
        IDatabaseService databaseService,
        ISetupStore setupStore,
        IBikeStore bikeStore)
    {
        this.databaseService = databaseService;
        this.setupStore = setupStore;
        this.bikeStore = bikeStore;

        // Ignore only the synchronous replay that can happen during
        // subscription. If a store starts empty, its first real load must still
        // trigger a refresh so known boards can become enriched after startup.
        setupSubscription = SubscribeIgnoringSynchronousReplay(
            setupStore.Connect(),
            "setup store change");
        bikeSubscription = SubscribeIgnoringSynchronousReplay(
            bikeStore.Connect(),
            "bike store change");

        RequestRefresh("initial load");
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

    private static Setup SetupFromSnapshot(SetupSnapshot snapshot) => new(snapshot.Id, snapshot.Name)
    {
        BikeId = snapshot.BikeId,
        FrontSensorConfigurationJson = snapshot.FrontSensorConfigurationJson,
        RearSensorConfigurationJson = snapshot.RearSensorConfigurationJson,
        Updated = snapshot.Updated,
    };

    private static LiveDaqTravelCalibration CreateTravelCalibration(
        ISensorConfiguration? frontSensorConfiguration,
        RearTravelCalibration? rearTravelCalibration)
    {
        var front = CreateCalibration(frontSensorConfiguration);
        var rear = CreateCalibration(rearTravelCalibration);

        return new LiveDaqTravelCalibration(front, rear);
    }

    public void Dispose()
    {
        setupSubscription.Dispose();
        bikeSubscription.Dispose();
        refreshGate.Dispose();
        changesSubject.Dispose();
    }

    private IDisposable SubscribeIgnoringSynchronousReplay<T>(IObservable<T> source, string reason)
    {
        var ignoreSynchronousReplay = true;
        var subscription = source.Subscribe(_ =>
        {
            if (ignoreSynchronousReplay)
            {
                return;
            }

            RequestRefresh(reason);
        });

        ignoreSynchronousReplay = false;
        return subscription;
    }

    // Coalesces overlapping refresh triggers so three near-simultaneous store
    // changes produce at most two runs (the in-flight one, plus a single
    // follow-up that batches everything that arrived while it was running).
    private void RequestRefresh(string reason)
    {
        lock (coalesceGate)
        {
            if (refreshInProgress)
            {
                refreshPending = true;
                return;
            }

            refreshInProgress = true;
        }

        _ = RefreshLoopAsync(reason);
    }

    private async Task RefreshLoopAsync(string initialReason)
    {
        var reason = initialReason;
        while (true)
        {
            await RefreshAsync(reason).ConfigureAwait(false);

            lock (coalesceGate)
            {
                if (!refreshPending)
                {
                    refreshInProgress = false;
                    return;
                }

                refreshPending = false;
                reason = "coalesced follow-up";
            }
        }
    }

    private async Task RefreshAsync(string reason)
    {
        await refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            logger.Debug("Refreshing known live DAQ boards because of {RefreshReason}", reason);

            var boards = await databaseService.GetAllAsync<Board>().ConfigureAwait(false);

            var projections = boards
                .OrderBy(board => board.Id)
                .Select(BuildProjection)
                .ToArray();
            var records = projections.Select(projection => projection.Record).ToArray();

            currentProjections = projections.ToDictionary(projection => projection.Record.IdentityKey, StringComparer.OrdinalIgnoreCase);
            changesSubject.OnNext(records);
            logger.Debug("Refreshed known live DAQ boards with {RecordCount} records", records.Length);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Refreshing known live DAQ boards failed; keeping the last snapshot");
            // No query-level error surface exists yet. Leave the last
            // successful snapshot intact and retry on the next change.
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private KnownLiveDaqProjection BuildProjection(Board board)
    {
        var setupSnapshot = board.SetupId.HasValue
            ? setupStore.Get(board.SetupId.Value)
            : null;

        setupSnapshot ??= setupStore.FindByBoardId(board.Id);

        var bikeSnapshot = setupSnapshot is null
            ? null
            : bikeStore.Get(setupSnapshot.BikeId);

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

        var setup = SetupFromSnapshot(setupSnapshot);
        var bike = Bike.FromSnapshot(bikeSnapshot);
        var frontSensorConfiguration = setup.FrontSensorConfiguration(bike);
        RearTravelCalibrationBuilder.TryBuild(setup, bike, out var rearTravelCalibration, out _);
        var calibration = CreateTravelCalibration(frontSensorConfiguration, rearTravelCalibration);

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
                BikeData: TelemetryBikeData.Create(bike, frontSensorConfiguration, rearTravelCalibration),
                TravelCalibration: calibration));
    }

    private static LiveDaqTravelChannelCalibration? CreateCalibration(Models.SensorConfigurations.ISensorConfiguration? configuration)
    {
        if (configuration is null)
        {
            return null;
        }

        return configuration.MaxTravel <= 0
            ? null
            : new LiveDaqTravelChannelCalibration(configuration.MaxTravel, configuration.MeasurementToTravel);
    }

    private static LiveDaqTravelChannelCalibration? CreateCalibration(Models.SensorConfigurations.RearTravelCalibration? calibration)
    {
        if (calibration is null || calibration.MaxTravel <= 0)
        {
            return null;
        }

        return new LiveDaqTravelChannelCalibration(calibration.MaxTravel, calibration.MeasurementToTravel);
    }
}