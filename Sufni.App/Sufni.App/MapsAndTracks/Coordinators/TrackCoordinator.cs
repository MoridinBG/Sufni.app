using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Linq;
using Sufni.App.MapsAndTracks.Queries;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Shared.Models;
using Serilog;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Sessions.Models;
namespace Sufni.App.MapsAndTracks.Coordinators;

public class TrackCoordinator : ITrackCoordinator, IDisposable
{
    private readonly ITrackRepository trackRepository;
    private readonly ISynchronizableRepository<Track> trackEntityRepository;
    private readonly ISessionRepository sessionRepository;
    private readonly IFilesService filesService;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionTrackReader sessionTrackReader;
    private readonly IFullTrackPointReader fullTrackPointReader;
    private readonly IGpxTrackReader gpxTrackReader;
    private readonly ITrackStoreWriter trackStore;
    private readonly ISessionStoreWriter sessionStore;
    private readonly IUiThreadDispatcher dispatcher;
    private readonly IDisposable metricSubscription;
    private readonly Dictionary<Guid, MetricsRun> metricsRuns = [];
    private readonly HashSet<Task> metricTasks = [];
    private readonly CancellationTokenSource metricShutdown = new();
    private Task? metricPlanning;
    private bool metricPlanningPending;
    private bool stopping;
    private bool disposed;

    public TrackCoordinator(
        ITrackRepository trackRepository,
        ISynchronizableRepository<Track> trackEntityRepository,
        ISessionRepository sessionRepository,
        IFilesService filesService,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISessionTrackReader sessionTrackReader,
        IFullTrackPointReader fullTrackPointReader,
        IGpxTrackReader gpxTrackReader,
        ITrackStoreWriter trackStore,
        ISessionStoreWriter sessionStore,
        IUiThreadDispatcher dispatcher,
        StorePublication publication)
    {
        this.trackRepository = trackRepository;
        this.trackEntityRepository = trackEntityRepository;
        this.sessionRepository = sessionRepository;
        this.filesService = filesService;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.sessionTrackReader = sessionTrackReader;
        this.fullTrackPointReader = fullTrackPointReader;
        this.gpxTrackReader = gpxTrackReader;
        this.trackStore = trackStore;
        this.sessionStore = sessionStore;
        this.dispatcher = dispatcher;
        metricSubscription = publication.Committed.Subscribe(commit =>
        {
            if (commit.Stores.Contains(sessionStore) || commit.Stores.Contains(trackStore)) ReconcileMetrics();
        });
        dispatcher.Post(ReconcileMetrics);
    }

    private static readonly ILogger logger = Log.ForContext<TrackCoordinator>();
    private const double DefaultMediaColumnWidth = 400.0;

    private sealed record MetricsRun(Guid SessionId, GpsMetricsKey Key, SessionTrackKey? Track)
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void ReconcileMetrics()
    {
        if (stopping) return;
        metricPlanningPending = true;
        if (metricPlanning is not null) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        metricPlanning = completion.Task;
        metricTasks.Add(completion.Task);
        _ = PlanMetricsAsync(completion);
    }

    private async Task PlanMetricsAsync(TaskCompletionSource completion)
    {
        var cancellationToken = metricShutdown.Token;
        try
        {
            while (true)
            {
                var capture = await dispatcher.InvokeAsync<(IReadOnlyList<SessionSnapshot> Sessions, IReadOnlyList<TrackSnapshot> Tracks)?>(() =>
                {
                    if (stopping || !metricPlanningPending) return null;
                    metricPlanningPending = false;
                    return (sessionStore.GetAll(), trackStore.GetAll());
                });
                if (capture is not { } snapshots) break;
                var inputs = await backgroundTaskRunner.RunAsync(() => snapshots.Sessions.Select(session =>
                {
                    var input = SessionGpsMetricsQuery.Evaluate(session.Timestamp, session.DurationSeconds,
                        session.GpsOffsetSeconds, session.WindowStartSeconds, snapshots.Tracks);
                    return (Session: session, input.Key, input.Track);
                }).ToDictionary(input => input.Session.Id), cancellationToken);
                await dispatcher.InvokeAsync(() =>
                {
                    if (!stopping && !metricPlanningPending) ApplyMetricInputs(inputs);
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.Warning(exception, "GPS metric inputs could not be evaluated");
        }
        finally
        {
            await dispatcher.InvokeAsync(() =>
            {
                metricPlanning = null;
                metricTasks.Remove(completion.Task);
                completion.TrySetResult();
                if (metricPlanningPending && !stopping) ReconcileMetrics();
            });
        }
    }

    private void ApplyMetricInputs(IReadOnlyDictionary<Guid,
        (SessionSnapshot Session, GpsMetricsKey Key, SessionTrackKey? Track)> inputs)
    {
        foreach (var run in metricsRuns.Values.ToArray())
            if (!inputs.TryGetValue(run.SessionId, out var current) || current.Key != run.Key)
                run.Cancellation.Cancel();

        foreach (var (id, current) in inputs)
        {
            if (current.Session.GpsMetricsKey == current.Key) continue;
            if (metricsRuns.TryGetValue(id, out var existing) && existing.Key == current.Key &&
                !existing.Cancellation.IsCancellationRequested) continue;
            var run = new MetricsRun(id, current.Key, current.Track);
            metricsRuns[id] = run;
            metricTasks.Add(run.Completion.Task);
            _ = ComputeMetricsAsync(run);
        }
    }

    private async Task ComputeMetricsAsync(MetricsRun run)
    {
        try
        {
            var committed = await backgroundTaskRunner.RunAsync(async () =>
            {
                var token = run.Cancellation.Token;
                token.ThrowIfCancellationRequested();
                IReadOnlyList<TrackPoint>? points = null;
                if (run.Track is { } key)
                {
                    points = await sessionTrackReader.GetSessionTrackAsync(key, token);
                    if (points is null) return (GpsMetrics?)null;
                }
                var metrics = GpsMetricsCalculator.Calculate(points);
                token.ThrowIfCancellationRequested();
                return await trackRepository.TryCommitGpsMetricsAsync(run.SessionId, run.Key, metrics, token)
                    ? metrics : null;
            }, run.Cancellation.Token);
            if (committed is null) return;
            await dispatcher.InvokeAsync(async () =>
            {
                if (disposed || sessionStore.Get(run.SessionId) is not { } current) return;
                var input = SessionGpsMetricsQuery.Evaluate(current.Timestamp, current.DurationSeconds,
                    current.GpsOffsetSeconds, current.WindowStartSeconds, trackStore.GetAll());
                if (input.Key != run.Key) return;
                await sessionStore.PublishSessionsChangedAsync([current with
                {
                    GpsMetricsKey = run.Key,
                    DistanceMeters = committed.DistanceMeters,
                    AscentMeters = committed.AscentMeters,
                    DescentMeters = committed.DescentMeters
                }]);
            });
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.Warning(exception, "GPS metrics could not be refreshed for session {SessionId}", run.SessionId);
        }
        finally
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (metricsRuns.GetValueOrDefault(run.SessionId) == run) metricsRuns.Remove(run.SessionId);
                metricTasks.Remove(run.Completion.Task);
                run.Cancellation.Dispose();
                run.Completion.TrySetResult();
            });
        }
    }

    public Task StopAsync() => dispatcher.InvokeAsync(async () =>
    {
        stopping = true;
        metricShutdown.Cancel();
        metricSubscription.Dispose();
        foreach (var run in metricsRuns.Values.ToArray()) run.Cancellation.Cancel();
        await Task.WhenAll(metricTasks.ToArray());
    });

    public void Dispose()
    {
        disposed = true;
        stopping = true;
        metricShutdown.Cancel();
        metricSubscription.Dispose();
        foreach (var run in metricsRuns.Values.ToArray()) run.Cancellation.Cancel();
        metricShutdown.Dispose();
    }

    public async Task<GpxImportResult> ImportGpxAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var files = await filesService.OpenGpxFilesAsync();
            if (files.Count == 0) return new GpxImportResult.Canceled();

            return await backgroundTaskRunner.RunAsync(
                () => ImportGpxCoreAsync(files, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.Error(e, "GPX import could not start");
            return new GpxImportResult.Failed(GpxImportFailureReason.Import, e.Message);
        }
    }

    public Task<SessionTrackPresentationData> LoadSessionTrackAsync(
        Guid sessionId,
        TelemetryData telemetryData,
        CancellationToken cancellationToken = default)
    {
        return backgroundTaskRunner.RunAsync(
            () => LoadSessionTrackCoreAsync(sessionId, telemetryData, cancellationToken),
            cancellationToken);
    }

    private async Task<GpxImportResult> ImportGpxCoreAsync(
        IReadOnlyList<PickedFile> files,
        CancellationToken cancellationToken)
    {
        var importedCount = 0;
        var alreadyImportedCount = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await gpxTrackReader.ReadAsync(file, cancellationToken);
                if (result is GpxTrackReadResult.Failed failure)
                    return new GpxImportResult.Failed(failure.Reason, failure.Details, importedCount, alreadyImportedCount);
                var track = new Track(((GpxTrackReadResult.Read)result).Points);

                var existingTrack = await trackEntityRepository.GetAsync(track.Id);
                if (existingTrack is not null)
                {
                    alreadyImportedCount++;
                    continue;
                }

                await trackEntityRepository.PutAsync(track);
                await trackStore.PublishTracksChangedAsync(await trackRepository.GetTrackSnapshotsAsync([track.Id]));
                importedCount++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.Error(e, "GPX import failed for {FileName}", file.Name);
                return new GpxImportResult.Failed(GpxImportFailureReason.Import, e.Message,
                    importedCount, alreadyImportedCount);
            }
        }

        return new GpxImportResult.Completed(importedCount, alreadyImportedCount);
    }

    private async Task<SessionTrackPresentationData> LoadSessionTrackCoreAsync(
        Guid sessionId,
        TelemetryData telemetryData,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = await sessionRepository.GetSessionAsync(sessionId);
        if (session is null) return new SessionTrackPresentationData(null, null, null, null);
        var timestamp = session.Timestamp ?? telemetryData.Metadata.Timestamp;
        var duration = session.DurationSeconds ?? telemetryData.Metadata.Duration;
        var gpsOffset = SessionTrackTimeline.NormalizeGpsOffsetSeconds(session.GpsOffsetSeconds);
        var selected = SessionTrackSelection.Select(trackStore.GetAll(), timestamp, duration, gpsOffset);
        if (selected is null) return new SessionTrackPresentationData(null, null, null, null);

        var fullPoints = await fullTrackPointReader.GetTrackPointsExactAsync(selected.Id, selected.PointsRevision, cancellationToken);
        if (fullPoints is null) return new SessionTrackPresentationData(selected.Id, null, null, null);
        var key = new SessionTrackKey(selected.Id, selected.PointsRevision, timestamp, duration, gpsOffset);
        var points = await sessionTrackReader.GetSessionTrackAsync(key, cancellationToken);
        return new SessionTrackPresentationData(selected.Id, AsList(fullPoints), AsNullableList(points), DefaultMediaColumnWidth);
    }

    private static List<TrackPoint> AsList(IReadOnlyList<TrackPoint> points)
    {
        return points switch
        {
            List<TrackPoint> list => list,
            _ => points.ToList(),
        };
    }

    private static List<TrackPoint>? AsNullableList(IReadOnlyList<TrackPoint>? points)
    {
        return points switch
        {
            null => null,
            List<TrackPoint> list => list,
            _ => points.ToList(),
        };
    }
}

public abstract record GpxImportResult
{
    private GpxImportResult() { }
    public sealed record Canceled : GpxImportResult;
    public sealed record Completed(int ImportedCount, int AlreadyImportedCount) : GpxImportResult;
    public sealed record Failed(GpxImportFailureReason Reason, string? Details = null,
        int ImportedCount = 0, int AlreadyImportedCount = 0) : GpxImportResult;
}
