using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Terrain.Services;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Coordinators;

public interface ITerrainProfileCoordinator
{
    void Prioritize(Guid sessionId);
    Task StopAsync();
}

internal sealed class TerrainProfileCoordinator : ITerrainProfileCoordinator, IDisposable
{
    private sealed record Request(Guid Id, TerrainProfileKey Key);

    private static readonly ILogger Logger = Log.ForContext<TerrainProfileCoordinator>();
    private readonly ISessionStoreWriter sessions;
    private readonly ITrackStore tracks;
    private readonly ISessionContentReader content;
    private readonly IFullTrackPointReader points;
    private readonly ITerrainProfileRepository repository;
    private readonly IBackgroundTaskRunner background;
    private readonly IUiThreadDispatcher dispatcher;
    private readonly IDisposable subscription;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<Request> queue = [];
    private readonly List<Guid> priorities = [];
    private Request? active;
    private CancellationTokenSource? activeCancellation;
    private Task? computation;
    private Task? planning;
    private long generation;
    private bool stopped;

    public TerrainProfileCoordinator(ISessionStoreWriter sessions, ITrackStore tracks, ISessionContentReader content,
        IFullTrackPointReader points, ITerrainProfileRepository repository, IBackgroundTaskRunner background,
        IUiThreadDispatcher dispatcher, StorePublication publication)
    {
        this.sessions = sessions;
        this.tracks = tracks;
        this.content = content;
        this.points = points;
        this.repository = repository;
        this.background = background;
        this.dispatcher = dispatcher;
        subscription = publication.Committed.Where(change => change.Stores.Contains(sessions) || change.Stores.Contains(tracks))
            .Subscribe(_ => Reconcile());
        dispatcher.Post(Reconcile);
    }

    private void Reconcile()
    {
        if (stopped) return;
        generation++;
        if (planning is not null) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        planning = completion.Task;
        _ = PlanAsync(generation, completion);
    }

    private async Task PlanAsync(long requested, TaskCompletionSource completion)
    {
        try
        {
            var snapshot = sessions.GetAll();
            var trackSnapshot = tracks.GetAll();
            var pending = await background.RunAsync(() => snapshot.OrderByDescending(s => s.Timestamp).ThenBy(s => s.Id)
                .Select(s => (s.Id, Terrain: SessionTerrainStatusQuery.Evaluate(s, trackSnapshot)))
                .Where(s => s.Terrain.Status is TerrainStatus.Pending)
                .Select(s => new Request(s.Id, s.Terrain.Key)).ToList(), lifetime.Token).ConfigureAwait(false);
            await dispatcher.InvokeAsync(() =>
            {
                if (stopped || requested != generation) return;
                if (active is not null && !pending.Contains(active)) activeCancellation?.Cancel();
                queue.Clear();
                queue.AddRange(pending.Where(request => request != active || activeCancellation?.IsCancellationRequested == true));
                priorities.RemoveAll(id => queue.All(request => request.Id != id));
            });
        }
        catch (OperationCanceledException) when (stopped) { }
        catch (Exception error)
        {
            Logger.Warning(error, "Could not plan terrain profiles");
            await dispatcher.InvokeAsync(() => { activeCancellation?.Cancel(); queue.Clear(); });
        }
        finally
        {
            await dispatcher.InvokeAsync(() =>
            {
                planning = null;
                completion.TrySetResult();
                if (requested != generation) Reconcile();
                else StartNext();
            });
        }
    }

    public void Prioritize(Guid sessionId)
    {
        if (stopped || active?.Id == sessionId) return;
        priorities.Remove(sessionId);
        priorities.Insert(0, sessionId);
    }

    private void StartNext()
    {
        if (stopped || active is not null || planning is not null || queue.Count == 0) return;
        var request = priorities.Count == 0 ? queue[0] : queue.FirstOrDefault(r => r.Id == priorities[0]) ?? queue[0];
        priorities.Remove(request.Id);
        queue.Remove(request);
        active = request;
        activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        computation = completion.Task;
        _ = RunAsync(request, activeCancellation.Token, completion);
    }

    private async Task RunAsync(Request request, CancellationToken token, TaskCompletionSource completion)
    {
        try
        {
            var result = await background.RunAsync(() => ComputeAndCommitAsync(request, token), token).ConfigureAwait(false);
            if (result is null) return;
            await dispatcher.InvokeAsync(() => sessions.Get(request.Id) is { } session
                ? sessions.PublishSessionsChangedAsync([session with
                {
                    TerrainProfileKey = request.Key, TerrainOutcome = result.Outcome, TerrainSummary = result.Summary,
                    TerrainClassKey = null, TerrainClassSummary = null
                }], token)
                : Task.CompletedTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Logger.Warning(error, "Could not publish terrain for {SessionId}", request.Id);
        }
        finally
        {
            await dispatcher.InvokeAsync(() =>
            {
                activeCancellation!.Dispose();
                activeCancellation = null;
                active = null;
                computation = null;
                completion.TrySetResult();
                StartNext();
            });
        }
    }

    private async Task<TerrainMaterialization?> ComputeAndCommitAsync(Request request, CancellationToken token)
    {
        var value = await CalculateAsync(request, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (value is null) return null;
        try
        {
            return await repository.TryCommitAsync(request.Id, request.Key, value, token).ConfigureAwait(false)
                ? value : null;
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            Logger.Warning(error, "Could not persist terrain for {SessionId}", request.Id);
            return TerrainMaterialization.WithoutProfile(TerrainOutcome.Failed);
        }
    }

    private async Task<TerrainMaterialization?> CalculateAsync(Request request, CancellationToken token)
    {
        if (request.Key.Content.TrackKey is not { } track)
            return TerrainMaterialization.WithoutProfile(TerrainOutcome.NoTrack);
        try
        {
            var read = await content.GetExactSnapshotAsync(request.Id, request.Key.Content,
                SessionContentSelection.ProcessedTelemetry, token).ConfigureAwait(false);
            if (read is not SessionContentSnapshotResult.Available { Snapshot.ProcessedTelemetry: { } telemetry })
                return null;
            var fixes = await points.GetTrackPointsExactAsync(track.TrackId, track.PointsRevision, token)
                .ConfigureAwait(false);
            if (fixes is null) return TerrainMaterialization.WithoutProfile(TerrainOutcome.ContentMissing);
            var timeline = SessionTrackTimeline.FromSessionWindow(track.Timestamp, track.GpsOffsetSeconds, track.WindowOriginFraction);
            var samples = TrackSampleAdapter.FromOriginalFixes(fixes, timeline, track.DurationSeconds);
            return TerrainProfileCalculator.Calculate(telemetry, samples, token) switch
            {
                TerrainProfileResult.Computed computed => TerrainMaterialization.Computed(computed.Profile),
                TerrainProfileResult.Unavailable { Reason: TerrainUnavailableReason.NoTrack } =>
                    TerrainMaterialization.WithoutProfile(TerrainOutcome.NoTrack),
                TerrainProfileResult.Unavailable => TerrainMaterialization.WithoutProfile(TerrainOutcome.NoMovement)
            };
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            Logger.Warning(error, "Could not derive terrain for {SessionId}", request.Id);
            return TerrainMaterialization.WithoutProfile(TerrainOutcome.Failed);
        }
    }

    public async Task StopAsync()
    {
        var pending = await dispatcher.InvokeAsync<Task>(() =>
        {
            Stop();
            return Task.WhenAll(planning ?? Task.CompletedTask, computation ?? Task.CompletedTask);
        });
        await pending.ConfigureAwait(false);
    }

    private void Stop()
    {
        if (stopped) return;
        stopped = true;
        subscription.Dispose();
        lifetime.Cancel();
        queue.Clear();
    }

    public void Dispose()
    {
        Stop();
        lifetime.Dispose();
    }
}
