using System.Reactive.Linq;
using System.Reactive.Subjects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Sufni.Telemetry;
using Serilog;

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
    IUiThreadDispatcher dispatcher) : IImportSessionsCoordinator, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<ImportSessionsCoordinator>();
    private static readonly int ImportProcessDop = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    private readonly BehaviorSubject<SessionImportState> state = new(new SessionImportState.Idle());
    private Task? activeRun;
    private readonly CancellationTokenSource shutdown = new();

    public SessionImportState State => state.Value;

    public IObservable<SessionImportState> Observe() => state.AsObservable();

    public async Task ImportAsync(IReadOnlyList<ITelemetryFile> files, Guid setupId, CancellationToken cancellationToken = default)
    {
        var run = await dispatcher.InvokeAsync<Task>(() =>
        {
            if (activeRun is { IsCompleted: false }) return activeRun;

            shutdown.Token.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            state.OnNext(new SessionImportState.Running(0, 0, []));
            activeRun = RunImportAsync(files.ToArray(), setupId, cancellationToken);
            return activeRun;
        });
        await run;
    }

    private async Task RunImportAsync(IReadOnlyList<ITelemetryFile> files, Guid setupId, CancellationToken cancellationToken)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, cancellationToken);
        logger.Information(
            "Starting session import for {FileCount} files and setup {SetupId}",
            files.Count,
            setupId);

        try
        {
            var progress = new UiThreadProgress<SessionImportEvent>(dispatcher, ReportProgress);
            var result = await backgroundTaskRunner.RunAsync(
                () => ImportCoreAsync(files, setupId, progress, runCancellation.Token));

            logger.Information(
                "Session import completed with {ImportedCount} imported sessions and {FailureCount} failures",
                result.Imported.Count,
                result.Failures.Count);
            await dispatcher.InvokeAsync(() =>
                state.OnNext(new SessionImportState.Completed(result, State.Events)));
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            await dispatcher.InvokeAsync(() => state.OnNext(new SessionImportState.Idle()));
        }
        catch (Exception e)
        {
            logger.Error(e, "Session import failed for setup {SetupId}", setupId);
            await dispatcher.InvokeAsync(() =>
                state.OnNext(new SessionImportState.Failed(e.Message, State.Events)));
        }
    }

    private void ReportProgress(SessionImportEvent message)
    {
        var running = (SessionImportState.Running)State;
        state.OnNext(message is SessionImportEvent.Progress progress
            ? running with { Current = progress.Current, Total = progress.Total }
            : running with { Events = running.Events.Add(message) });
    }

    public async Task StopAsync()
    {
        await shutdown.CancelAsync();
        var run = await dispatcher.InvokeAsync<Task?>(() => activeRun);
        if (run is not null) await run;
    }

    public void Dispose()
    {
        shutdown.Dispose();
        state.Dispose();
    }

    private async Task<SessionImportResult> ImportCoreAsync(
        IReadOnlyList<ITelemetryFile> files,
        Guid setupId,
        IProgress<SessionImportEvent>? progress,
        CancellationToken cancellationToken)
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

        IDaqManagementSession? batchSession = null;
        try
        {
            var networkFiles = files.OfType<NetworkTelemetryFile>().ToArray();
            if (networkFiles.Length > 0)
            {
                var endpoint = networkFiles[0].EndPoint;
                batchSession = await daqManagementService.OpenSessionAsync(
                    endpoint.Address.ToString(), endpoint.Port, cancellationToken);
                foreach (var networkFile in networkFiles)
                {
                    networkFile.AttachSession(batchSession);
                }
            }

            var channel = Channel.CreateBounded<(ITelemetryFile File, TelemetryFileSource Source)>(
                new BoundedChannelOptions(4)
                {
                    SingleReader = false,
                    SingleWriter = true,
                });
            var totalToProcess = files.Count(f => f.ShouldBeImported is not false);
            var processedCount = 0;
            var progressGate = new object();
            var committedImports = new ConcurrentBag<(ITelemetryFile File, SessionSnapshot Snapshot, RecordedSessionSourceSnapshot Source)>();
            var failuresQueue = new ConcurrentQueue<SessionImportFailure>();

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
                    case SessionImportFailureOperation.Publish:
                        Report(new SessionImportEvent.PublicationFailed(telemetryFile.Name, e.Message));
                        break;
                }
            }

            async Task RunProducerAsync()
            {
                foreach (var telemetryFile in files)
                {
                    if (cancellationToken.IsCancellationRequested) break;
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
                        telemetrySource = await telemetryFile.ReadSourceAsync(cancellationToken);
                        await channel.Writer.WriteAsync((telemetryFile, telemetrySource));
                        telemetrySource = null;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
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
                            committedImports.Add((telemetryFile, snapshot, RecordedSessionSourceSnapshot.From(source)));
                        }
                        catch (Exception e)
                        {
                            logger.Warning(e, "Failed to import telemetry file {FileName}", telemetryFile.Name);
                            AddFailure(telemetryFile, e, SessionImportFailureOperation.Import);
                        }
                    }
                }
            }

            // Shutdown stops intake; queued files finish persistence, publication and acknowledgement.
            var consumerTasks = Enumerable.Range(0, ImportProcessDop)
                .Select(_ => RunConsumerAsync())
                .ToArray();
            Exception? producerException = null;
            try
            {
                await RunProducerAsync();
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

            var committedEntries = committedImports.ToArray();
            imported.AddRange(committedEntries.Select(entry => entry.Snapshot));
            failures.AddRange(failuresQueue);

            var publicationSucceeded = true;
            var importedSessionIds = committedEntries.Select(entry => entry.Snapshot.Id).ToArray();
            if (importedSessionIds.Length > 0)
            {
                try
                {
                    await sessionStore.PublishSessionsChangedAsync(importedSessionIds);
                    await sourceStore.PublishSourcesChangedAsync(committedEntries.Select(entry => entry.Source).ToArray());
                }
                catch (Exception e)
                {
                    publicationSucceeded = false;
                    logger.Warning(e, "Failed to publish imported session store updates");
                    foreach (var entry in committedEntries)
                    {
                        failures.Add(new SessionImportFailure(
                            entry.File.Name,
                            e.Message,
                            SessionImportFailureOperation.Publish));
                        Report(new SessionImportEvent.PublicationFailed(entry.File.Name, e.Message));
                    }
                }
            }

            if (publicationSucceeded)
            {
                foreach (var entry in committedEntries)
                {
                    Report(new SessionImportEvent.Imported(entry.Snapshot));
                    try
                    {
                        await entry.File.OnImported();
                    }
                    catch (Exception e)
                    {
                        logger.Warning(e, "Failed to finish post-import action for telemetry file {FileName}", entry.File.Name);
                        failures.Add(new SessionImportFailure(
                            entry.File.Name,
                            e.Message,
                            SessionImportFailureOperation.Import));
                        Report(new SessionImportEvent.ImportFailed(entry.File.Name, e.Message));
                    }
                }
            }
        }
        finally
        {
            if (batchSession is not null)
            {
                await batchSession.DisposeAsync();
            }
        }

        return new SessionImportResult(imported, failures);
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
    IReadOnlyList<SessionImportFailure> Failures);

public enum SessionImportFailureOperation
{
    Import,
    Publish,
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
    public sealed record PublicationFailed(string FileName, string ErrorMessage) : SessionImportEvent;
    public sealed record TrashFailed(string FileName, string ErrorMessage) : SessionImportEvent;
    public sealed record Progress(int Current, int Total) : SessionImportEvent;
}
