using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Sufni.Telemetry;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;
namespace Sufni.App.Acquisition.Coordinators;

/// <summary>
/// Coordinates importing telemetry files into recorded sessions.
/// It reads the raw SST source, builds processed telemetry from it, and
/// persists both the canonical raw source and the derived session data.
/// </summary>
public class ImportSessionsCoordinator(
    ISessionTelemetryWriter sessionTelemetryWriter,
    ISynchronizableRepository<Setup> setupRepository,
    ISynchronizableRepository<Bike> bikeRepository,
    ISessionStoreWriter sessionStore,
    IRecordedSessionSourceStoreWriter sourceStore,
    IBackgroundTaskRunner backgroundTaskRunner,
    IDaqManagementService daqManagementService,
    IRecordedSessionReprocessor reprocessor,
    IEditorFactory editorFactory) : IImportSessionsCoordinator
{
    private static readonly ILogger logger = Log.ForContext<ImportSessionsCoordinator>();
    private static readonly int ImportProcessDop = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    public Task OpenAsync()
    {
        editorFactory.OpenImportSessions();
        return Task.CompletedTask;
    }

    public async Task<SessionImportResult> ImportAsync(
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
        var failures = new List<SessionImportFailure>();

        logger.Verbose("Loading setup {SetupId} for session import", setupId);
        var setup = await setupRepository.GetAsync(setupId)
            ?? throw new Exception("Setup is missing");
        logger.Verbose("Loading bike {BikeId} for imported setup {SetupId}", setup.BikeId, setupId);
        var bike = await bikeRepository.GetAsync(setup.BikeId)
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

            var lanes = BuildImportLanes(files, sessions);
            var channel = Channel.CreateBounded<(ITelemetryFile File, TelemetryFileSource Source)>(
                new BoundedChannelOptions(4)
                {
                    SingleReader = false,
                    SingleWriter = false,
                });
            var totalToProcess = files.Count(f => f.ShouldBeImported is not false);
            var processedCount = 0;
            var progressGate = new object();
            var importedSnapshots = new ConcurrentBag<SessionSnapshot>();
            var failuresQueue = new ConcurrentQueue<SessionImportFailure>();
            var acknowledgements = new ConcurrentQueue<ITelemetryFile>();

            void Report(SessionImportEvent importEvent)
            {
                if (progress is null)
                {
                    return;
                }

                lock (progressGate)
                {
                    try
                    {
                        progress.Report(importEvent);
                    }
                    catch (Exception e)
                    {
                        logger.Warning(e, "Session import progress callback failed");
                    }
                }
            }

            void ReportProgress()
            {
                lock (progressGate)
                {
                    processedCount++;
                    if (progress is null)
                    {
                        return;
                    }

                    try
                    {
                        progress.Report(new SessionImportEvent.Progress(processedCount, totalToProcess));
                    }
                    catch (Exception e)
                    {
                        logger.Warning(e, "Session import progress callback failed");
                    }
                }
            }

            void AddFailure(ITelemetryFile telemetryFile, Exception e, SessionImportFailureOperation operation)
            {
                failuresQueue.Enqueue(new SessionImportFailure(telemetryFile.Name, e.Message, operation));
                switch (operation)
                {
                    case SessionImportFailureOperation.Import:
                        Report(new SessionImportEvent.ImportFailed(telemetryFile.Name, e.Message));
                        break;
                    case SessionImportFailureOperation.Trash:
                        Report(new SessionImportEvent.TrashFailed(telemetryFile.Name, e.Message));
                        break;
                }
            }

            async Task RunProducerAsync(IReadOnlyList<ITelemetryFile> lane)
            {
                foreach (var telemetryFile in lane)
                {
                    // Legacy semantics: only HasValue && Value is "import";
                    // !HasValue is "trash"; HasValue && !Value is "leave alone".
                    if (telemetryFile.ShouldBeImported is false)
                    {
                        continue;
                    }

                    if (telemetryFile.ShouldBeImported is null)
                    {
                        ReportProgress();
                        try
                        {
                            logger.Verbose("Trashing telemetry file {FileName}", telemetryFile.Name);
                            await telemetryFile.OnTrashed();
                        }
                        catch (Exception e)
                        {
                            logger.Warning(e, "Failed to trash telemetry file {FileName}", telemetryFile.Name);
                            AddFailure(telemetryFile, e, SessionImportFailureOperation.Trash);
                        }

                        continue;
                    }

                    TelemetryFileSource? telemetrySource = null;
                    try
                    {
                        logger.Verbose("Reading source data for {FileName}", telemetryFile.Name);
                        telemetrySource = await telemetryFile.ReadSourceAsync();
                        await channel.Writer.WriteAsync((telemetryFile, telemetrySource));
                        telemetrySource = null;
                    }
                    catch (Exception e)
                    {
                        ReportProgress();
                        logger.Warning(e, "Failed to read telemetry file {FileName}", telemetryFile.Name);
                        AddFailure(telemetryFile, e, SessionImportFailureOperation.Import);
                    }
                    finally
                    {
                        telemetrySource?.Dispose();
                    }
                }
            }

            async Task RunConsumerAsync()
            {
                await foreach (var (telemetryFile, telemetrySource) in channel.Reader.ReadAllAsync())
                {
                    using (telemetrySource)
                    {
                        ReportProgress();
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
                                failuresQueue.Enqueue(new SessionImportFailure(
                                    telemetryFile.Name,
                                    malformedMessage,
                                    SessionImportFailureOperation.Import));
                                Report(new SessionImportEvent.ImportFailed(telemetryFile.Name, malformedMessage));
                                continue;
                            }

                            var source = RecordedSessionSourceFactory.CreateImportedSst(Guid.NewGuid(), telemetrySource);
                            var session = new Session(
                                id: source.SessionId,
                                name: telemetryFile.Name,
                                description: telemetryFile.Description,
                                setup: setupId,
                                timestamp: ((DateTimeOffset)telemetryFile.StartTime).ToUnixTimeSeconds());

                            var domain = CreateImportDomain(session, setupSnapshot, bikeSnapshot, source);
                            logger.Verbose("Reprocessing imported source for {FileName}", telemetryFile.Name);
                            var reprocessResult = await reprocessor.ReprocessAsync(domain, source);

                            logger.Verbose("Persisting imported session for {FileName}", telemetryFile.Name);
                            var persisted = await sessionTelemetryWriter.PutProcessedSessionAsync(
                                session,
                                reprocessResult.ProcessedTelemetry,
                                reprocessResult.GeneratedFullTrack,
                                source);

                            var snapshot = SessionSnapshot.From(persisted);
                            importedSnapshots.Add(snapshot);
                            acknowledgements.Enqueue(telemetryFile);
                            Report(new SessionImportEvent.Imported(snapshot));
                        }
                        catch (Exception e)
                        {
                            logger.Warning(e, "Failed to import telemetry file {FileName}", telemetryFile.Name);
                            AddFailure(telemetryFile, e, SessionImportFailureOperation.Import);
                        }
                    }
                }
            }

            var consumerTasks = Enumerable.Range(0, ImportProcessDop)
                .Select(_ => RunConsumerAsync())
                .ToArray();
            var producerTasks = lanes.Select(RunProducerAsync).ToArray();
            Exception? producerException = null;
            try
            {
                await Task.WhenAll(producerTasks);
                channel.Writer.Complete();
            }
            catch (Exception e)
            {
                producerException = e;
                channel.Writer.TryComplete(e);
            }

            try
            {
                await Task.WhenAll(consumerTasks);
            }
            catch when (producerException is not null)
            {
                // Consumers observe the producer fault through the completed channel.
            }

            if (producerException is not null)
            {
                ExceptionDispatchInfo.Capture(producerException).Throw();
            }

            imported.AddRange(importedSnapshots);
            failures.AddRange(failuresQueue);

            var importedSessionIds = imported.Select(snapshot => snapshot.Id).ToArray();
            if (importedSessionIds.Length > 0)
            {
                try
                {
                    await sessionStore.PublishSessionsChangedAsync(importedSessionIds);
                    await sourceStore.PublishSourcesChangedAsync(importedSessionIds);
                }
                catch (Exception e)
                {
                    logger.Warning(e, "Failed to publish imported session store updates");
                    foreach (var telemetryFile in acknowledgements)
                    {
                        failures.Add(new SessionImportFailure(
                            telemetryFile.Name,
                            e.Message,
                            SessionImportFailureOperation.Import));
                        Report(new SessionImportEvent.ImportFailed(telemetryFile.Name, e.Message));
                    }
                }
            }

            while (acknowledgements.TryDequeue(out var telemetryFile))
            {
                try
                {
                    await telemetryFile.OnImported();
                }
                catch (Exception e)
                {
                    logger.Warning(e, "Failed to finish post-import action for telemetry file {FileName}", telemetryFile.Name);
                    failures.Add(new SessionImportFailure(
                        telemetryFile.Name,
                        e.Message,
                        SessionImportFailureOperation.Import));
                    Report(new SessionImportEvent.ImportFailed(telemetryFile.Name, e.Message));
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

    private static List<IReadOnlyList<ITelemetryFile>> BuildImportLanes(
        IReadOnlyList<ITelemetryFile> files,
        IReadOnlyDictionary<IPEndPoint, IDaqManagementSession> sessions)
    {
        var lanes = new List<IReadOnlyList<ITelemetryFile>>();
        var networkLanes = new Dictionary<IPEndPoint, List<ITelemetryFile>>();
        var localLane = new List<ITelemetryFile>();

        foreach (var file in files)
        {
            if (file is NetworkTelemetryFile networkFile && sessions.ContainsKey(networkFile.EndPoint))
            {
                if (!networkLanes.TryGetValue(networkFile.EndPoint, out var lane))
                {
                    lane = [];
                    networkLanes.Add(networkFile.EndPoint, lane);
                    lanes.Add(lane);
                }

                lane.Add(file);
            }
            else
            {
                localLane.Add(file);
            }
        }

        if (localLane.Count > 0)
        {
            lanes.Add(localLane);
        }

        return lanes;
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
            DerivationWindow: null,
            new SessionStaleness.UnknownLegacyFingerprint(),
            DerivedChangeKind.None);
    }
}

public sealed record SessionImportResult(
    IReadOnlyList<SessionSnapshot> Imported,
    IReadOnlyList<SessionImportFailure> Failures);

public enum SessionImportFailureOperation
{
    Import,
    Trash,
}

public sealed record SessionImportFailure(
    string FileName,
    string ErrorMessage,
    SessionImportFailureOperation Operation);

public abstract record SessionImportEvent
{
    private SessionImportEvent() { }

    public sealed record Imported(SessionSnapshot Snapshot) : SessionImportEvent;
    public sealed record ImportFailed(string FileName, string ErrorMessage) : SessionImportEvent;
    public sealed record TrashFailed(string FileName, string ErrorMessage) : SessionImportEvent;
    public sealed record Progress(int Current, int Total) : SessionImportEvent;
}
