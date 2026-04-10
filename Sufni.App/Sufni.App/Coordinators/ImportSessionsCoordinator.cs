using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.Telemetry;

namespace Sufni.App.Coordinators;

public sealed class ImportSessionsCoordinator(
    IDatabaseService databaseService,
    ISessionStoreWriter sessionStore,
    IShellCoordinator shell,
    IBackgroundTaskRunner backgroundTaskRunner,
    Func<ImportSessionsViewModel> importSessionsResolver) : IImportSessionsCoordinator
{
    public Task OpenAsync()
    {
        shell.OpenOrFocus<ImportSessionsViewModel>(
            _ => true,
            importSessionsResolver);
        return Task.CompletedTask;
    }

    public async Task<SessionImportResult> ImportAsync(
        ITelemetryDataStore dataStore,
        IReadOnlyList<ITelemetryFile> files,
        Guid setupId,
        IProgress<SessionImportEvent>? progress = null)
    {
        return await backgroundTaskRunner.RunAsync(
            () => ImportCoreAsync(files, setupId, progress));
    }

    private async Task<SessionImportResult> ImportCoreAsync(
        IReadOnlyList<ITelemetryFile> files,
        Guid setupId,
        IProgress<SessionImportEvent>? progress)
    {
        var imported = new List<SessionSnapshot>();
        var failures = new List<(string FileName, string ErrorMessage)>();

        var setup = await databaseService.GetAsync<Setup>(setupId)
            ?? throw new Exception("Setup is missing");
        var bike = await databaseService.GetAsync<Bike>(setup.BikeId)
            ?? throw new Exception("Bike is missing");

        var bikeData = CreateBikeData(setup, bike);

        foreach (var telemetryFile in files)
        {
            // Legacy semantics: only HasValue && Value is "import";
            // !HasValue is "trash"; HasValue && !Value is "leave alone".
            if (telemetryFile.ShouldBeImported is true)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(telemetryFile.MalformedMessage))
                    {
                        var malformedMessage = telemetryFile.MalformedMessage;
                        failures.Add((telemetryFile.Name, malformedMessage));
                        progress?.Report(new SessionImportEvent.Failed(telemetryFile.Name, malformedMessage));
                        continue;
                    }

                    var psst = await telemetryFile.GeneratePsstAsync(bikeData);

                    var session = new Session(
                        id: Guid.NewGuid(),
                        name: telemetryFile.Name,
                        description: telemetryFile.Description,
                        setup: setupId,
                        timestamp: (int)((DateTimeOffset)telemetryFile.StartTime).ToUnixTimeSeconds())
                    {
                        ProcessedData = psst
                    };

                    await databaseService.PutSessionAsync(session);
                    await telemetryFile.OnImported();

                    var snapshot = SessionSnapshot.From(session);
                    sessionStore.Upsert(snapshot);
                    imported.Add(snapshot);
                    progress?.Report(new SessionImportEvent.Imported(snapshot));
                }
                catch (Exception e)
                {
                    failures.Add((telemetryFile.Name, e.Message));
                    progress?.Report(new SessionImportEvent.Failed(telemetryFile.Name, e.Message));
                }
            }
            else if (telemetryFile.ShouldBeImported is null)
            {
                try
                {
                    await telemetryFile.OnTrashed();
                }
                catch (Exception e)
                {
                    failures.Add((telemetryFile.Name, e.Message));
                    progress?.Report(new SessionImportEvent.Failed(telemetryFile.Name, e.Message));
                }
            }
        }

        return new SessionImportResult(imported, failures);
    }

    private static BikeData CreateBikeData(Setup setup, Bike bike)
    {
        var frontSensorConfiguration = setup.FrontSensorConfiguration(bike);
        var rearSensorConfiguration = setup.RearSensorConfiguration(bike);

        return new BikeData(
            bike.HeadAngle,
            frontSensorConfiguration?.MaxTravel,
            rearSensorConfiguration?.MaxTravel,
            frontSensorConfiguration?.MeasurementToTravel,
            rearSensorConfiguration?.MeasurementToTravel);
    }
}
