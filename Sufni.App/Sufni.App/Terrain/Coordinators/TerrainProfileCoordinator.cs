using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    private sealed record ClassRun(Guid SetupId, TerrainScaleKey Scale, ImmutableDictionary<Guid, TerrainProfileKey> Sessions);
    private sealed record Plan(List<Request> Profiles, IReadOnlyDictionary<Guid, TerrainScaleKey> Scales, List<ClassRun> Classes);

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
    private readonly List<ClassRun> classQueue = [];
    private Request? active;
    private ClassRun? activeClasses;
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
            var plan = await background.RunAsync(() => Evaluate(snapshot, trackSnapshot), lifetime.Token).ConfigureAwait(false);
            await dispatcher.InvokeAsync(() =>
            {
                if (stopped || requested != generation) return;
                if (active is not null && !plan.Profiles.Contains(active)) activeCancellation?.Cancel();
                if (activeClasses is { } running && plan.Scales.GetValueOrDefault(running.SetupId) != running.Scale)
                    activeCancellation?.Cancel();
                var runningClasses = activeCancellation?.IsCancellationRequested == true ? null : activeClasses;
                queue.Clear();
                queue.AddRange(plan.Profiles.Where(request => request != active || activeCancellation?.IsCancellationRequested == true));
                priorities.RemoveAll(id => queue.All(request => request.Id != id));
                classQueue.Clear();
                classQueue.AddRange(plan.Classes
                    .Select(run => run.SetupId == runningClasses?.SetupId
                        ? run with { Sessions = run.Sessions.RemoveRange(runningClasses.Sessions.Keys) }
                        : run)
                    .Where(run => run.Sessions.Count > 0));
            });
        }
        catch (OperationCanceledException) when (stopped) { }
        catch (Exception error)
        {
            Logger.Warning(error, "Could not plan terrain profiles");
            await dispatcher.InvokeAsync(() => { activeCancellation?.Cancel(); queue.Clear(); classQueue.Clear(); });
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

    private static Plan Evaluate(IReadOnlyList<SessionSnapshot> snapshot, IReadOnlyList<TrackSnapshot> trackSnapshot)
    {
        var evaluated = snapshot.OrderByDescending(s => s.Timestamp).ThenBy(s => s.Id)
            .Select(s => (Session: s, Terrain: SessionTerrainStatusQuery.Evaluate(s, trackSnapshot))).ToList();
        var profiles = evaluated.Where(s => s.Terrain.Status is TerrainStatus.Pending)
            .Select(s => new Request(s.Session.Id, s.Terrain.Key)).ToList();
        var scales = SetupTerrainScaleQuery.Evaluate(snapshot, trackSnapshot)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Key);
        var classes = evaluated.Where(s => s.Session.SetupId is { } setupId && scales.ContainsKey(setupId))
            .GroupBy(s => s.Session.SetupId!.Value)
            .Select(setup => new ClassRun(setup.Key, scales[setup.Key], setup
                .Where(s => SessionTerrainStatusQuery.EvaluateClasses(s.Session, s.Terrain.Status, scales[setup.Key])
                    is TerrainClassState.Pending)
                .ToImmutableDictionary(s => s.Session.Id, s => s.Terrain.Key)))
            .Where(run => run.Sessions.Count > 0).ToList();
        return new(profiles, scales, classes);
    }

    public void Prioritize(Guid sessionId)
    {
        if (stopped || active?.Id == sessionId) return;
        priorities.Remove(sessionId);
        priorities.Insert(0, sessionId);
    }

    private void StartNext()
    {
        if (stopped || computation is not null || planning is not null) return;
        if (queue.Count == 0)
        {
            StartClasses();
            return;
        }
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

    private void StartClasses()
    {
        if (classQueue.Count == 0) return;
        var run = classQueue[0];
        classQueue.RemoveAt(0);
        activeClasses = run;
        activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        computation = completion.Task;
        _ = RunClassesAsync(run, activeCancellation.Token, completion);
    }

    private async Task RunClassesAsync(ClassRun run, CancellationToken token, TaskCompletionSource completion)
    {
        try
        {
            var written = await background.RunAsync(() => ClassifyAndCommitAsync(run, token), token).ConfigureAwait(false);
            if (written.Count == 0) return;
            await dispatcher.InvokeAsync(() => sessions.PublishSessionsChangedAsync([.. written
                .Select(pair => sessions.Get(pair.Key) is { } session
                    ? session with { TerrainClassKey = pair.Value.Key, TerrainClassSummary = pair.Value.Summary }
                    : null)
                .OfType<SessionSnapshot>()], token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Logger.Warning(error, "Could not classify terrain for setup {SetupId}", run.SetupId);
        }
        finally
        {
            await dispatcher.InvokeAsync(() =>
            {
                activeCancellation!.Dispose();
                activeCancellation = null;
                activeClasses = null;
                computation = null;
                completion.TrySetResult();
                StartNext();
            });
        }
    }

    private async Task<Dictionary<Guid, (TerrainClassKey Key, TerrainClassSummary Summary)>> ClassifyAndCommitAsync(
        ClassRun run, CancellationToken token)
    {
        var profiles = await repository.ReadAsync(run.Sessions, token).ConfigureAwait(false);
        var classes = new Dictionary<Guid, (TerrainClassKey Key, TerrainClassSummary Summary)>();
        foreach (var (id, profile) in profiles)
        {
            token.ThrowIfCancellationRequested();
            classes[id] = (new TerrainClassKey(run.Sessions[id], run.Scale, TerrainClassifier.Version),
                TerrainClassifier.Summarize(TerrainClassifier.Classify(profile, run.Scale, TerrainLabelSet.None)));
        }
        token.ThrowIfCancellationRequested();
        if (classes.Count == 0) return [];
        var written = await repository.CommitClassesAsync(classes, token).ConfigureAwait(false);
        return written.ToDictionary(id => id, id => classes[id]);
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
        classQueue.Clear();
    }

    public void Dispose()
    {
        Stop();
        lifetime.Dispose();
    }
}
