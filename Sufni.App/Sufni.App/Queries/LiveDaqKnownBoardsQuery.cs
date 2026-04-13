using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Serilog;

namespace Sufni.App.Queries;

// Keeps a cached, query-shaped view of known live DAQ boards so the coordinator can
// seed offline rows and refresh setup or bike labels cheaply.
public sealed class LiveDaqKnownBoardsQuery : ILiveDaqKnownBoardsQuery, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqKnownBoardsQuery>();

    private readonly IDatabaseService databaseService;
    private readonly ISetupStore setupStore;
    private readonly IBikeStore bikeStore;
    private readonly BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>> changesSubject = new([]);
    private IReadOnlyDictionary<string, KnownLiveDaqRecord> currentRecords = new Dictionary<string, KnownLiveDaqRecord>(StringComparer.OrdinalIgnoreCase);
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
            _ = RefreshAsync("setup store change");
        });
        bikeSubscription = bikeStore.Connect().Subscribe(changes =>
        {
            _ = RefreshAsync("bike store change");
        });

        _ = RefreshAsync("initial load");
    }

    public IObservable<IReadOnlyList<KnownLiveDaqRecord>> Changes => changesSubject;

    public KnownLiveDaqRecord? Get(string identityKey) =>
        currentRecords.TryGetValue(identityKey, out var record) ? record : null;

    public LiveDaqTravelCalibration? GetTravelCalibration(string identityKey)
    {
        if (!TryResolveSetupAndBike(identityKey, out var record, out var setup, out var bike))
        {
            return null;
        }

        var calibration = CreateTravelCalibration(setup, bike);

        return calibration is { Front: null, Rear: null }
            ? null
            : calibration;
    }

    public LiveDaqSessionContext? GetSessionContext(string identityKey)
    {
        if (!TryResolveSetupAndBike(identityKey, out var record, out var setup, out var bike))
        {
            return null;
        }

        var calibration = CreateTravelCalibration(setup, bike);
        return new LiveDaqSessionContext(
            IdentityKey: record.IdentityKey,
            BoardId: Guid.Parse(record.BoardId),
            DisplayName: record.DisplayName,
            SetupId: record.SetupId!.Value,
            SetupName: record.SetupName!,
            BikeId: record.BikeId!.Value,
            BikeName: record.BikeName!,
            BikeData: TelemetryBikeData.Create(setup, bike),
            TravelCalibration: calibration);
    }

    private bool TryResolveSetupAndBike(
        string identityKey,
        out KnownLiveDaqRecord record,
        out Setup setup,
        out Bike bike)
    {
        setup = null!;
        bike = null!;

        if (!currentRecords.TryGetValue(identityKey, out record!) ||
            record.SetupId is not Guid setupId ||
            record.BikeId is not Guid bikeId)
        {
            return false;
        }

        var setupSnapshot = setupStore.Get(setupId);
        var bikeSnapshot = bikeStore.Get(bikeId);
        if (setupSnapshot is null || bikeSnapshot is null)
        {
            return false;
        }

        setup = SetupFromSnapshot(setupSnapshot);
        bike = Bike.FromSnapshot(bikeSnapshot);
        return true;
    }

    private static Setup SetupFromSnapshot(SetupSnapshot snapshot) => new(snapshot.Id, snapshot.Name)
    {
        BikeId = snapshot.BikeId,
        FrontSensorConfigurationJson = snapshot.FrontSensorConfigurationJson,
        RearSensorConfigurationJson = snapshot.RearSensorConfigurationJson,
        Updated = snapshot.Updated,
    };

    private static LiveDaqTravelCalibration CreateTravelCalibration(Setup setup, Bike bike)
    {
        var front = CreateCalibration(setup.FrontSensorConfiguration(bike));
        var rear = CreateCalibration(setup.RearSensorConfiguration(bike));

        return new LiveDaqTravelCalibration(front, rear);
    }

    public void Dispose()
    {
        setupSubscription.Dispose();
        bikeSubscription.Dispose();
        refreshGate.Dispose();
        changesSubject.Dispose();
    }

    private async Task RefreshAsync(string reason)
    {
        await refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            logger.Debug("Refreshing known live DAQ boards because of {RefreshReason}", reason);

            var boards = await databaseService.GetAllAsync<Board>().ConfigureAwait(false);

            var records = boards
                .OrderBy(board => board.Id)
                .Select(BuildRecord)
                .ToArray();

            currentRecords = records.ToDictionary(record => record.IdentityKey, StringComparer.OrdinalIgnoreCase);
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
}