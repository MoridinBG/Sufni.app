using Sufni.App.Video.Stores;
using Sufni.App.Video.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Stores;

namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>Derives recorded-session read models from committed store snapshots.</summary>
public sealed class RecordedSessionProjection : IRecordedSessionProjection, IDisposable
{
    private readonly ISessionStore sessionStore;
    private readonly ITrackStore trackStore;
    private long trackGeneration;
    private readonly ISessionVideoStore videoStore;
    private long videoGeneration;
    private readonly ISetupStore setupStore;
    private readonly IBikeStore bikeStore;
    private readonly IRecordedSessionSourceStore sourceStore;
    private readonly IProcessingDependencyHashIndex dependencyHashIndex;
    private readonly IProcessingFingerprintService fingerprintService;
    private readonly IRecordedSessionProjectionScheduler scheduler;
    private readonly IUiThreadDispatcher dispatcher;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly StorePublication publication;
    private readonly SourceCache<RecordedSessionSummary, Guid> summaries = new(summary => summary.Id);
    private readonly Dictionary<Guid, Guid?> sessionToSetup = [];
    private readonly Dictionary<Guid, RecordedSessionDomainSnapshot> domains = [];
    private readonly Dictionary<Guid, SessionWatch> watches = [];
    private readonly CompositeDisposable subscriptions = [];
    private readonly System.Threading.Lock stateGate = new();
    private readonly HashSet<Guid> pendingRecomputeIds = [];
    private readonly HashSet<Guid> removedSessionIds = [];
    private bool recomputeFlushScheduled;
    private bool disposed;

    public RecordedSessionProjection(
        ISessionStore sessionStore, ISetupStore setupStore, IBikeStore bikeStore,
        IRecordedSessionSourceStore sourceStore, IProcessingFingerprintService fingerprintService,
        IProcessingDependencyHashIndex dependencyHashIndex, IUiThreadDispatcher dispatcher,
        IBackgroundTaskRunner backgroundTaskRunner, StorePublication publication, ITrackStore trackStore, ISessionVideoStore videoStore)
        : this(sessionStore, setupStore, bikeStore, sourceStore, fingerprintService,
            dependencyHashIndex, dispatcher, backgroundTaskRunner, publication, trackStore, videoStore,
            new UiThreadRecordedSessionProjectionScheduler(dispatcher))
    {
    }

    internal RecordedSessionProjection(
        ISessionStore sessionStore, ISetupStore setupStore, IBikeStore bikeStore,
        IRecordedSessionSourceStore sourceStore, IProcessingFingerprintService fingerprintService,
        IProcessingDependencyHashIndex dependencyHashIndex, IUiThreadDispatcher dispatcher,
        IBackgroundTaskRunner backgroundTaskRunner, StorePublication publication, ITrackStore trackStore, ISessionVideoStore videoStore,
        IRecordedSessionProjectionScheduler scheduler)
    {
        this.sessionStore = sessionStore;
        this.trackStore = trackStore;
        this.videoStore = videoStore;
        this.setupStore = setupStore;
        this.bikeStore = bikeStore;
        this.sourceStore = sourceStore;
        this.fingerprintService = fingerprintService;
        this.dependencyHashIndex = dependencyHashIndex;
        this.dispatcher = dispatcher;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.publication = publication;
        this.scheduler = scheduler;
        subscriptions.Add(summaries.Connect().Subscribe(_ => publication.RecordChange(this)));
        subscriptions.Add(sessionStore.Connect().Subscribe(ApplySessionChanges));
        subscriptions.Add(setupStore.Connect().Subscribe(changes =>
            QueueForSetups(changes.Where(change => change.Reason is ChangeReason.Add or ChangeReason.Remove).Select(change => change.Key))));
        subscriptions.Add(sourceStore.Connect().Subscribe(ApplySourceChanges));
        subscriptions.Add(dependencyHashIndex.Connect().Subscribe(change => QueueForSetups([change.SetupId])));
        subscriptions.Add(trackStore.Connect().Subscribe(_ =>
        {
            lock (stateGate)
            {
                if (disposed) return;
                trackGeneration++;
                pendingRecomputeIds.UnionWith(sessionToSetup.Keys);
            }
        }));
        subscriptions.Add(videoStore.Connect().Subscribe(_ =>
        {
            lock (stateGate)
            {
                if (disposed) return;
                videoGeneration++;
                pendingRecomputeIds.UnionWith(sessionToSetup.Keys);
            }
        }));
        subscriptions.Add(publication.Committed.Subscribe(ApplyCommit));
        SchedulePending();
    }

    public IObservable<IChangeSet<RecordedSessionSummary, Guid>> ConnectSessions() => summaries.Connect();

    public IObservable<RecordedSessionDomainSnapshot> WatchSession(Guid sessionId) =>
        Observable.Create<RecordedSessionDomainSnapshot>(observer =>
        {
            SessionWatch watch;
            IDisposable subscription;
            lock (stateGate)
            {
                if (disposed) return Observable.Empty<RecordedSessionDomainSnapshot>().Subscribe(observer);
                if (!watches.TryGetValue(sessionId, out watch!))
                {
                    watch = new SessionWatch();
                    watches.Add(sessionId, watch);
                    if (domains.TryGetValue(sessionId, out var domain)) watch.Subject.OnNext(domain);
                }
                watch.Subscribers++;
                subscription = watch.Subject.Subscribe(observer);
            }
            return Disposable.Create(() =>
            {
                subscription.Dispose();
                lock (stateGate)
                {
                    if (--watch.Subscribers == 0 && watches.GetValueOrDefault(sessionId) == watch)
                    {
                        watches.Remove(sessionId);
                        watch.Subject.Dispose();
                    }
                }
            });
        });

    public void Dispose()
    {
        SessionWatch[] completed;
        lock (stateGate)
        {
            disposed = true;
            pendingRecomputeIds.Clear();
            removedSessionIds.Clear();
            sessionToSetup.Clear();
            domains.Clear();
            completed = watches.Values.ToArray();
            watches.Clear();
        }
        subscriptions.Dispose();
        summaries.Dispose();
        CompleteWatches(completed);
    }

    private void ApplySessionChanges(IChangeSet<SessionSnapshot, Guid> changes)
    {
        lock (stateGate)
        {
            if (disposed) return;
            foreach (var change in changes)
            {
                if (change.Reason == ChangeReason.Remove)
                {
                    sessionToSetup.Remove(change.Key);
                    pendingRecomputeIds.Remove(change.Key);
                    removedSessionIds.Add(change.Key);
                }
                else if (change.Reason != ChangeReason.Moved)
                {
                    sessionToSetup[change.Key] = change.Current.SetupId;
                    removedSessionIds.Remove(change.Key);
                    pendingRecomputeIds.Add(change.Key);
                }
            }
        }
    }

    private void QueueForSetups(IEnumerable<Guid> setupIds)
    {
        lock (stateGate)
        {
            if (disposed) return;
            var affected = setupIds.ToHashSet();
            pendingRecomputeIds.UnionWith(sessionToSetup
                .Where(pair => pair.Value is { } setupId && affected.Contains(setupId))
                .Select(pair => pair.Key));
        }
    }

    private void ApplySourceChanges(IChangeSet<RecordedSessionSourceSnapshot, Guid> changes)
    {
        lock (stateGate)
        {
            if (disposed) return;
            var sourceIds = changes.Select(change => change.Key).ToHashSet();
            pendingRecomputeIds.UnionWith(sessionToSetup.Keys.Where(id =>
                sessionStore.Get(id) is { } session && sourceIds.Contains(session.RecordingSourceSessionId)));
        }
    }

    private void ApplyCommit(StorePublicationCommit commit)
    {
        if (!commit.Stores.Contains(sessionStore) && !commit.Stores.Contains(setupStore) &&
            !commit.Stores.Contains(bikeStore) && !commit.Stores.Contains(sourceStore) && !commit.Stores.Contains(trackStore) && !commit.Stores.Contains(videoStore)) return;

        lock (stateGate)
        {
            if (disposed) return;
            if (removedSessionIds.Count > 0)
            {
                var completed = new List<SessionWatch>();
                using var batch = publication.BeginBatch();
                publication.MutateAsync(this, summaries.SuspendNotifications, () => summaries.Edit(updater =>
                {
                    foreach (var id in removedSessionIds)
                    {
                        domains.Remove(id);
                        updater.RemoveKey(id);
                        if (watches.Remove(id, out var watch)) completed.Add(watch);
                    }
                })).GetAwaiter().GetResult();
                removedSessionIds.Clear();
                CompleteWatches(completed);
            }
        }
        SchedulePending();
    }

    private void SchedulePending()
    {
        lock (stateGate)
        {
            if (disposed || recomputeFlushScheduled || pendingRecomputeIds.Count == 0) return;
            recomputeFlushScheduled = true;
        }
        scheduler.Post(FlushPendingRecomputes);
    }

    private async void FlushPendingRecomputes()
    {
        ProjectionInput[] inputs;
        lock (stateGate)
        {
            if (disposed) return;
            var tracks = trackStore.GetAll();
            var clips = videoStore.GetAll();
            inputs = pendingRecomputeIds.Select(id => Capture(id, tracks, clips)).OfType<ProjectionInput>().ToArray();
            pendingRecomputeIds.Clear();
        }
        (ProjectionInput Input, RecordedSessionDomainSnapshot Domain, SessionVideoSnapshot? Video)[] results;
        try
        {
            results = await backgroundTaskRunner.RunAsync(() => inputs.Select(input =>
            {
                var domain = RecordedSessionDomainSnapshotFactory.Create(
                    input.Session, input.Setup, input.Bike, input.Source, fingerprintService, input.DependencyHash,
                    SessionTrackSelection.Select(input.Tracks, input.Session.Timestamp, input.Session.DurationSeconds, input.Session.GpsOffsetSeconds));
                var video = SessionVideoSelection.ForWindow(input.Clips, input.Source?.SourceHash,
                    new SessionTrackTimeline(0, OwnerOriginSeconds: input.Session.WindowStartSeconds), input.Session.DurationSeconds)
                    .MaxBy(clip => clip.Added);
                return (Input: input, Domain: domain, Video: video);
            }).ToArray()).ConfigureAwait(false);
        }
        catch
        {
            await dispatcher.InvokeAsync(FinishFlush);
            throw;
        }
        await dispatcher.InvokeAsync(() =>
        {
            try
            {
                lock (stateGate)
                {
                    if (disposed) return;
                    var current = results.Where(result => IsCurrent(result.Input)).ToArray();
                    using var batch = publication.BeginBatch();
                    // Cache mutations finish synchronously inside the dispatcher-owned batch.
                    publication.MutateAsync(this, summaries.SuspendNotifications, () => summaries.Edit(updater =>
                    {
                        foreach (var (_, domain, video) in current)
                        {
                            domains[domain.Session.Id] = domain;
                            pendingRecomputeIds.Remove(domain.Session.Id);
                            var session = domain.Session;
                            updater.AddOrUpdate(new RecordedSessionSummary(
                                session.Id, session.Updated, session.Name, session.Description,
                                session.Timestamp, session.HasProcessedData, domain.Staleness,
                                session.DurationSeconds, session.DistanceMeters, session.AscentMeters, session.DescentMeters, video));
                        }
                    })).GetAwaiter().GetResult();
                    foreach (var (_, domain, _) in current)
                        if (watches.TryGetValue(domain.Session.Id, out var watch)) watch.Subject.OnNext(domain);
                }
            }
            finally
            {
                FinishFlush();
            }
        });
    }

    private void FinishFlush()
    {
        lock (stateGate) recomputeFlushScheduled = false;
        SchedulePending();
    }

    private ProjectionInput? Capture(Guid id, IReadOnlyList<TrackSnapshot> tracks, IReadOnlyList<SessionVideoSnapshot> clips)
    {
        if (sessionStore.Get(id) is not { } session) return null;
        var setup = session.SetupId is { } setupId ? setupStore.Get(setupId) : null;
        var bike = setup is null ? null : bikeStore.Get(setup.BikeId);
        return new ProjectionInput(session, setup, bike, sourceStore.Get(session.RecordingSourceSessionId),
            setup is null ? null : dependencyHashIndex.GetForSetup(setup.Id), tracks, trackGeneration, clips, videoGeneration);
    }

    private bool IsCurrent(ProjectionInput input)
    {
        var current = Capture(input.Session.Id, input.Tracks, input.Clips);
        if (current is null) return false;
        if (input.VideoGeneration == current.VideoGeneration && input.TrackGeneration == current.TrackGeneration && input.Session == current.Session && input.Setup == current.Setup &&
            input.Bike == current.Bike && input.Source == current.Source && input.DependencyHash == current.DependencyHash)
            return true;
        pendingRecomputeIds.Add(input.Session.Id);
        return false;
    }

    private static void CompleteWatches(IEnumerable<SessionWatch> completed)
    {
        foreach (var watch in completed)
        {
            watch.Subject.OnCompleted();
            watch.Subject.Dispose();
        }
    }

    private sealed class SessionWatch
    {
        public ReplaySubject<RecordedSessionDomainSnapshot> Subject { get; } = new(1);
        public int Subscribers;
    }

    private sealed record ProjectionInput(SessionSnapshot Session, SetupSnapshot? Setup, BikeSnapshot? Bike,
        RecordedSessionSourceSnapshot? Source, string? DependencyHash, IReadOnlyList<TrackSnapshot> Tracks, long TrackGeneration,
        IReadOnlyList<SessionVideoSnapshot> Clips, long VideoGeneration);


}

internal interface IRecordedSessionProjectionScheduler
{
    void Post(Action action);
}

/// <summary>
/// Dispatcher-backed scheduler for deferred recorded-session projection recomputes.
/// The fixed scheduler keeps projection flush timing independent from the ambient
/// synchronization context of the thread that queued a change; background
/// priority keeps recompute flushes from competing with input and render work.
/// </summary>
internal sealed class UiThreadRecordedSessionProjectionScheduler(IUiThreadDispatcher uiThreadDispatcher) : IRecordedSessionProjectionScheduler
{
    public void Post(Action action) => uiThreadDispatcher.Post(action, UiDispatchPriority.Background);
}
