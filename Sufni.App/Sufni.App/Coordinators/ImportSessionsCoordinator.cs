using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.SessionGraph;
using Sufni.App.Services;
using Sufni.App.Services.Management;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.Telemetry;
using Serilog;

namespace Sufni.App.Coordinators;

/// <summary>
/// Coordinates importing telemetry files into recorded sessions.
/// It reads the raw SST source, builds processed telemetry from it, and
/// persists both the canonical raw source and the derived session data.
/// </summary>
public class ImportSessionsCoordinator(
    IDatabaseService databaseService,
    ISessionStoreWriter sessionStore,
    IRecordedSessionSourceStoreWriter sourceStore,
    IShellCoordinator shell,
    IBackgroundTaskRunner backgroundTaskRunner,
    IDaqManagementService daqManagementService,
    IRecordedSessionReprocessor reprocessor,
    Func<ImportSessionsViewModel> importSessionsResolver)
{
    private static readonly ILogger logger = Log.ForContext<ImportSessionsCoordinator>();

    public virtual Task OpenAsync()
    {
        shell.OpenOrFocus<ImportSessionsViewModel>(
            _ => true,
            importSessionsResolver);
        return Task.CompletedTask;
    }

    public virtual async Task<SessionImportResult> ImportAsync(
        IReadOnlyList<ITelemetryFile> files,
        Guid setupId,
        IProgress<SessionImportEvent>? progress = null)
    {
        logger.Information(
            "Starting session import for {FileCount} files and setup {SetupId}",
            files.Count,
            setupId);

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

        var setupSnapshot = SetupSnapshot.From(setup, boardId: null);
        var bikeSnapshot = BikeSnapshot.From(bike);

        var sessions = new Dictionary<IPEndPoint, IDaqManagementSession>();
        try
        {
            foreach (var networkFile in files.OfType<NetworkTelemetryFile>())
            {
                if (sessions.ContainsKey(networkFile.EndPoint))
                {
                    networkFile.AttachSession(sessions[networkFile.EndPoint]);
                    continue;
                }

                var session = await daqManagementService.OpenSessionAsync(
                    networkFile.EndPoint.Address.ToString(),
                    networkFile.EndPoint.Port);
                sessions[networkFile.EndPoint] = session;
                networkFile.AttachSession(session);
            }

            var totalToProcess = files.Count(f => f.ShouldBeImported is not false);
            var processedCount = 0;

            foreach (var telemetryFile in files)
            {
                // Legacy semantics: only HasValue && Value is "import";
                // !HasValue is "trash"; HasValue && !Value is "leave alone".
                if (telemetryFile.ShouldBeImported is true)
                {
                    processedCount++;
                    progress?.Report(new SessionImportEvent.Progress(processedCount, totalToProcess));
                    try
                    {
                        if (!telemetryFile.CanImport)
                        {
                            var malformedMessage = string.IsNullOrWhiteSpace(telemetryFile.MalformedMessage)
                                ? "The telemetry file cannot be imported."
                                : telemetryFile.MalformedMessage;
                            logger.Warning(
                                "Skipping malformed telemetry file {FileName}: {ErrorMessage}",
                                telemetryFile.Name,
                                malformedMessage);
                            failures.Add((telemetryFile.Name, malformedMessage));
                            progress?.Report(new SessionImportEvent.Failed(telemetryFile.Name, malformedMessage));
                            continue;
                        }

                        logger.Verbose("Reading source data for {FileName}", telemetryFile.Name);
                        var telemetrySource = await telemetryFile.ReadSourceAsync();
                        var source = CreateImportedSource(Guid.NewGuid(), telemetrySource);

                        var session = new Session(
                            id: source.SessionId,
                            name: telemetryFile.Name,
                            description: telemetryFile.Description,
                            setup: setupId,
                            timestamp: ((DateTimeOffset)telemetryFile.StartTime).ToUnixTimeSeconds());

                        var domain = CreateImportDomain(session, setupSnapshot, bikeSnapshot, source);
                        logger.Verbose("Reprocessing imported source for {FileName}", telemetryFile.Name);
                        var reprocessResult = await reprocessor.ReprocessAsync(domain, source);
                        session.ProcessedData = reprocessResult.TelemetryData.BinaryForm;
                        session.ProcessingFingerprintJson = AppJson.Serialize(reprocessResult.Fingerprint);

                        logger.Verbose("Persisting imported session for {FileName}", telemetryFile.Name);
                        var persisted = await databaseService.PutProcessedSessionAsync(
                            session,
                            reprocessResult.GeneratedFullTrack,
                            source);
                        await telemetryFile.OnImported();

                        var snapshot = SessionSnapshot.From(persisted);
                        sessionStore.Upsert(snapshot);
                        sourceStore.Upsert(RecordedSessionSourceSnapshot.From(source));
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
                    processedCount++;
                    progress?.Report(new SessionImportEvent.Progress(processedCount, totalToProcess));
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
        }
        finally
        {
            foreach (var session in sessions.Values)
            {
                await session.DisposeAsync();
            }
        }

        return new SessionImportResult(imported, failures);
    }

    private static RecordedSessionSource CreateImportedSource(Guid sessionId, TelemetryFileSource telemetrySource)
    {
        const int schemaVersion = 1;
        return new RecordedSessionSource
        {
            SessionId = sessionId,
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = telemetrySource.FileName,
            SchemaVersion = schemaVersion,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                telemetrySource.FileName,
                schemaVersion,
                telemetrySource.SstBytes),
            Payload = RecordedSessionSourcePayloadCodec.CompressImportedSst(telemetrySource.SstBytes)
        };
    }

    private static RecordedSessionDomainSnapshot CreateImportDomain(
        Session session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSource source)
    {
        var sessionSnapshot = SessionSnapshot.From(session);
        var sourceSnapshot = RecordedSessionSourceSnapshot.From(source);
        return new RecordedSessionDomainSnapshot(
            sessionSnapshot,
            setup,
            bike,
            CurrentFingerprint: null,
            PersistedFingerprint: null,
            sourceSnapshot,
            new SessionStaleness.UnknownLegacyFingerprint(),
            DerivedChangeKind.None);
    }
}

public sealed record SessionImportResult(
    IReadOnlyList<SessionSnapshot> Imported,
    IReadOnlyList<(string FileName, string ErrorMessage)> Failures);

public abstract record SessionImportEvent
{
    private SessionImportEvent() { }

    public sealed record Imported(SessionSnapshot Snapshot) : SessionImportEvent;
    public sealed record Failed(string FileName, string ErrorMessage) : SessionImportEvent;
    public sealed record Progress(int Current, int Total) : SessionImportEvent;
}
