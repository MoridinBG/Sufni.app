using System.Reactive.Linq;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;
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
using Sufni.App.Sessions.Services;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;
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
    ISessionRepository sessionRepository,
    ITrackRepository trackRepository,
    ISynchronizableRepository<Setup> setupRepository,
    ISynchronizableRepository<Bike> bikeRepository,
    ISessionStoreWriter sessionStore,
    IRecordedSessionSourceStoreWriter sourceStore,
    IBackgroundTaskRunner backgroundTaskRunner,
    IDaqManagementService daqManagementService,
    IRecordedSessionReprocessor reprocessor,
    IUiThreadDispatcher dispatcher,
    StorePublication publication,
    ITrackStoreWriter trackStore) : IImportSessionsCoordinator, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<ImportSessionsCoordinator>();
    private static readonly int ImportProcessDop = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    private readonly BehaviorSubject<SessionImportState> state = new(new SessionImportState.Idle());
    private Task? activeRun;
    private readonly CancellationTokenSource shutdown = new();

    public SessionImportState State => state.Value;

    public IObservable<SessionImportState> Observe() => state.AsObservable();

    public async Task ImportAsync(IReadOnlyList<SessionImportItem> files, Guid setupId, CancellationToken cancellationToken = default)
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

    private async Task RunImportAsync(IReadOnlyList<SessionImportItem> files, Guid setupId, CancellationToken cancellationToken)
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
                state.OnNext(new SessionImportState.Failed(SessionImportFailureReason.Batch, e.Message, State.Events)));
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
        IReadOnlyList<SessionImportItem> files,
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

        var setupSnapshot = SetupSnapshot.From(setup);
        var bikeSnapshot = BikeSnapshot.From(bike);

        IDaqManagementSession? batchSession = null;
        try
        {
            var networkFiles = files.Select(item => item.File).OfType<NetworkTelemetryFile>().ToArray();
            if (networkFiles.Length > 0)
            {
                var endpoint = networkFiles[0].EndPoint;
                batchSession = await daqManagementService.OpenSessionAsync(
                    endpoint, cancellationToken);
                foreach (var networkFile in networkFiles)
                {
                    networkFile.AttachSession(batchSession);
                }
            }

            var channel = Channel.CreateBounded<(SessionImportItem Item, TelemetryFileSource Source)>(
                new BoundedChannelOptions(4)
                {
                    SingleReader = false,
                    SingleWriter = true,
                });
            var totalToProcess = files.Count(f => f.Action != ImportFileAction.Ignore);
            var processedCount = 0;
            var progressGate = new object();
            var committedImports = new ConcurrentBag<(SessionImportItem Item, SessionSnapshot? Snapshot, RecordedSessionSourceSnapshot Source, bool AlreadyImported, Guid? TrackId)>();
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

            void ReportFailure(SessionImportFailure failure)
            {
                failuresQueue.Enqueue(failure);
                Report(new SessionImportEvent.Failed(failure));
            }

            void AddFailure(SessionImportItem item, Exception e, SessionImportFailureOperation operation)
            {
                var reason = operation switch
                {
                    SessionImportFailureOperation.Trash => SessionImportFailureReason.Trash,
                    SessionImportFailureOperation.Publish => SessionImportFailureReason.Publish,
                    _ => SessionImportFailureReason.Import
                };
                ReportFailure(new SessionImportFailure.OperationFailed(item.Name, operation, reason, e.Message));
            }

            async Task RunProducerAsync()
            {
                foreach (var item in files)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (item.Action == ImportFileAction.Ignore)
                    {
                        continue;
                    }

                    if (item.Action == ImportFileAction.Trash)
                    {
                        ReportProgress();
                        try
                        {
                            logger.Verbose("Trashing telemetry file {FileName}", item.Name);
                            await item.File.OnTrashed();
                        }
                        catch (Exception e)
                        {
                            logger.Warning(e, "Failed to trash telemetry file {FileName}", item.Name);
                            AddFailure(item, e, SessionImportFailureOperation.Trash);
                        }

                        continue;
                    }

                    TelemetryFileSource? telemetrySource = null;
                    try
                    {
                        logger.Verbose("Reading source data for {FileName}", item.Name);
                        telemetrySource = await item.File.ReadSourceAsync(cancellationToken);
                        await channel.Writer.WriteAsync((item, telemetrySource));
                        telemetrySource = null;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        ReportProgress();
                        logger.Warning(e, "Failed to read telemetry file {FileName}", item.Name);
                        AddFailure(item, e, SessionImportFailureOperation.Import);
                    }
                    finally
                    {
                        telemetrySource?.Dispose();
                    }
                }
            }

            async Task RunConsumerAsync()
            {
                await foreach (var (item, telemetrySource) in channel.Reader.ReadAllAsync())
                {
                    using (telemetrySource)
                    {
                        ReportProgress();
                        try
                        {
                            if (!item.File.CanImport)
                            {
                                var malformedMessage = string.IsNullOrWhiteSpace(item.File.MalformedMessage)
                                    ? "The telemetry file cannot be imported."
                                    : item.File.MalformedMessage;
                                logger.Warning(
                                    "Skipping malformed telemetry file {FileName}: {ErrorMessage}",
                                    item.Name,
                                    malformedMessage);
                                ReportFailure(new SessionImportFailure.OperationFailed(item.Name,
                                    SessionImportFailureOperation.Import, SessionImportFailureReason.InvalidFile, malformedMessage));
                                continue;
                            }

                            var source = RecordedSessionSourceFactory.CreateImportedSst(telemetrySource);
                            if (await sessionRepository.GetImportCheckpointAsync(source.SourceHash) is { } existing)
                            {
                                committedImports.Add((item,
                                    existing.Session is { } held ? SessionSnapshot.From(held) : null,
                                    existing.Source, true, null));
                                continue;
                            }
                            var session = new Session(
                                id: source.SessionId,
                                name: item.Name,
                                description: item.Description,
                                setup: setupId,
                                timestamp: ((DateTimeOffset)item.File.StartTime).ToUnixTimeSeconds());

                            var input = RecordedSessionDerivationInput.Create(
                                SessionSnapshot.From(session), setupSnapshot, bikeSnapshot, source);
                            logger.Verbose("Reprocessing imported source for {FileName}", item.Name);
                            var reprocessResult = await reprocessor.ReprocessAsync(input);
                            if (reprocessResult is RecordedSessionReprocessResult.Failed failure)
                            {
                                ReportFailure(new SessionImportFailure.ProcessingFailed(item.Name, failure.Reason, failure.Details));
                                continue;
                            }
                            var processed = (RecordedSessionReprocessResult.Processed)reprocessResult;

                            logger.Verbose("Persisting imported session for {FileName}", item.Name);
                            var generation = processed.Generation;
                            var newTrack = generation.TrackPoints is { Count: > 0 } points
                                ? new Track(points) : null;
                            var prepared = generation.CreateSession(SessionSnapshot.From(session));
                            var committed = await sessionRepository.ImportProcessedSessionAsync(prepared, newTrack, source);

                            var snapshot = committed.Session is { } persisted ? SessionSnapshot.From(persisted) : null;
                            committedImports.Add((item, snapshot, committed.Source, committed is SessionImportCommitResult.Exists, newTrack?.Id));
                        }
                        catch (Exception e)
                        {
                            logger.Warning(e, "Failed to import telemetry file {FileName}", item.Name);
                            AddFailure(item, e, SessionImportFailureOperation.Import);
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
            imported.AddRange(committedEntries.Where(entry => !entry.AlreadyImported).Select(entry => entry.Snapshot).OfType<SessionSnapshot>());
            failures.AddRange(failuresQueue);

            var publicationSucceeded = true;
            var importedSnapshots = committedEntries.Select(entry => entry.Snapshot).OfType<SessionSnapshot>().ToArray();
            if (committedEntries.Length > 0)
            {
                try
                {
                    var sourceSnapshots = committedEntries.Select(entry => entry.Source).ToArray();
                    var trackIds = committedEntries.Where(entry => entry.TrackId.HasValue)
                        .Select(entry => entry.TrackId!.Value).Distinct().ToArray();
                    // Track IDs are not stored on sessions; recovery republishes metadata without deriving it again.
                    var tracks = await trackRepository.GetTrackSnapshotsAsync(
                        committedEntries.Any(entry => entry.AlreadyImported) ? null : trackIds);
                    await dispatcher.InvokeAsync(async () =>
                    {
                        using var batch = publication.BeginBatch();
                        await sessionStore.PublishSessionsChangedAsync(importedSnapshots);
                        await sourceStore.PublishSourcesChangedAsync(sourceSnapshots);
                        await trackStore.PublishTracksChangedAsync(tracks);
                    });
                }
                catch (Exception e)
                {
                    publicationSucceeded = false;
                    logger.Warning(e, "Failed to publish imported session store updates");
                    foreach (var entry in committedEntries)
                    {
                        var failure = new SessionImportFailure.OperationFailed(entry.Item.Name,
                            SessionImportFailureOperation.Publish, SessionImportFailureReason.Publish, e.Message);
                        failures.Add(failure);
                        Report(new SessionImportEvent.Failed(failure));
                    }
                }
            }

            if (publicationSucceeded)
            {
                foreach (var entry in committedEntries)
                {
                    Report(entry.AlreadyImported
                        ? new SessionImportEvent.AlreadyImported(entry.Item.Name, entry.Source.SessionId)
                        : new SessionImportEvent.Imported(entry.Snapshot!));
                    try
                    {
                        await entry.Item.File.OnImported();
                    }
                    catch (Exception e)
                    {
                        logger.Warning(e, "Failed to finish post-import action for telemetry file {FileName}", entry.Item.Name);
                        Report(new SessionImportEvent.AcknowledgementFailed(entry.Item.Name, e.ToString()));
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

public abstract record SessionImportFailure
{
    private SessionImportFailure(string fileName, SessionImportFailureOperation operation)
    {
        FileName = fileName;
        Operation = operation;
    }
    public string FileName { get; }
    public SessionImportFailureOperation Operation { get; }

    public sealed record OperationFailed(string FileName, SessionImportFailureOperation Operation,
        SessionImportFailureReason Reason, string? Details = null) : SessionImportFailure(FileName, Operation);
    public sealed record ProcessingFailed(string FileName, SessionProcessingFailureReason Reason,
        string? Details = null) : SessionImportFailure(FileName, SessionImportFailureOperation.Import);
}

public abstract record SessionImportEvent
{
    private SessionImportEvent() { }

    public sealed record Imported(SessionSnapshot Snapshot) : SessionImportEvent;
    public sealed record AlreadyImported(string FileName, Guid SessionId) : SessionImportEvent;
    public sealed record AcknowledgementFailed(string FileName, string? Details) : SessionImportEvent;
    public sealed record Failed(SessionImportFailure Failure) : SessionImportEvent;
    public sealed record Progress(int Current, int Total) : SessionImportEvent;
}
