using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.Telemetry;
using Serilog;

namespace Sufni.App.Coordinators;

public sealed class ImportSessionsCoordinator(
    IDatabaseService databaseService,
    ISessionStoreWriter sessionStore,
    IShellCoordinator shell,
    IBackgroundTaskRunner backgroundTaskRunner,
    Func<ImportSessionsViewModel> importSessionsResolver) : IImportSessionsCoordinator
{
    private static readonly ILogger logger = Log.ForContext<ImportSessionsCoordinator>();

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
        logger.Information(
            "Starting session import for {FileCount} files and setup {SetupId}",
            files.Count,
            setupId);
        logger.Verbose(
            "Session import source is {DataStoreType}",
            dataStore.GetType().Name);

        try
        {
            var result = await backgroundTaskRunner.RunAsync(
                () => ImportCoreAsync(files, setupId, progress));

            logger.Information(
                "Session import completed with {ImportedCount} imported sessions and {FailureCount} failures",
                result.Imported.Count,
                result.Failures.Count);

            return result;
        }
        catch (Exception e)
        {
            logger.Error(e, "Session import failed for setup {SetupId}", setupId);
            throw;
        }
    }

    private async Task<SessionImportResult> ImportCoreAsync(
        IReadOnlyList<ITelemetryFile> files,
        Guid setupId,
        IProgress<SessionImportEvent>? progress)
    {
        var imported = new List<SessionSnapshot>();
        var failures = new List<(string FileName, string ErrorMessage)>();

        logger.Verbose("Loading setup {SetupId} for session import", setupId);
        var setup = await databaseService.GetAsync<Setup>(setupId)
            ?? throw new Exception("Setup is missing");
        logger.Verbose("Loading bike {BikeId} for imported setup {SetupId}", setup.BikeId, setupId);
        var bike = await databaseService.GetAsync<Bike>(setup.BikeId)
            ?? throw new Exception("Bike is missing");

        var bikeData = TelemetryBikeData.Create(setup, bike);

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
                        logger.Warning(
                            "Skipping malformed telemetry file {FileName}: {ErrorMessage}",
                            telemetryFile.Name,
                            malformedMessage);
                        failures.Add((telemetryFile.Name, malformedMessage));
                        progress?.Report(new SessionImportEvent.Failed(telemetryFile.Name, malformedMessage));
                        continue;
                    }

                    logger.Verbose("Generating processed session data for {FileName}", telemetryFile.Name);
                    var psst = await telemetryFile.GeneratePsstAsync(bikeData);

                    Guid? fullTrackId = null;
                    var telemetryData = TelemetryData.FromBinary(psst);
                    if (telemetryData.GpsData is { Length: > 0 })
                    {
                        var track = Track.FromGpsRecords(telemetryData.GpsData);
                        if (track.Points.Count > 0)
                        {
                            await databaseService.PutAsync(track);
                            fullTrackId = track.Id;
                        }
                    }

                    var session = new Session(
                        id: Guid.NewGuid(),
                        name: telemetryFile.Name,
                        description: telemetryFile.Description,
                        setup: setupId,
                        timestamp: (int)((DateTimeOffset)telemetryFile.StartTime).ToUnixTimeSeconds())
                    {
                        ProcessedData = psst,
                        FullTrack = fullTrackId
                    };

                    logger.Verbose("Persisting imported session for {FileName}", telemetryFile.Name);
                    await databaseService.PutSessionAsync(session);
                    await telemetryFile.OnImported();

                    var snapshot = SessionSnapshot.From(session);
                    sessionStore.Upsert(snapshot);
                    imported.Add(snapshot);
                    progress?.Report(new SessionImportEvent.Imported(snapshot));
                }
                catch (Exception e)
                {
                    logger.Warning(e, "Failed to import telemetry file {FileName}", telemetryFile.Name);
                    failures.Add((telemetryFile.Name, e.Message));
                    progress?.Report(new SessionImportEvent.Failed(telemetryFile.Name, e.Message));
                }
            }
            else if (telemetryFile.ShouldBeImported is null)
            {
                try
                {
                    logger.Verbose("Trashing telemetry file {FileName}", telemetryFile.Name);
                    await telemetryFile.OnTrashed();
                }
                catch (Exception e)
                {
                    logger.Warning(e, "Failed to trash telemetry file {FileName}", telemetryFile.Name);
                    failures.Add((telemetryFile.Name, e.Message));
                    progress?.Report(new SessionImportEvent.Failed(telemetryFile.Name, e.Message));
                }
            }
        }

        return new SessionImportResult(imported, failures);
    }
}
