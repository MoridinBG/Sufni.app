using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.Telemetry;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Reactive read model that keeps recorded-session derived state current.
/// It keeps the minimal fan-out indexes needed to coalesce bursts of changes,
/// then reads current store snapshots to publish coherent list summaries plus
/// per-session domain snapshots.
/// </summary>
public sealed class RecordedSessionProjection : IRecordedSessionProjection, IDisposable
{
    private readonly ISessionStore sessionStore;
    private readonly ISetupStore setupStore;
    private readonly IBikeStore bikeStore;
    private readonly IRecordedSessionSourceStore sourceStore;
    private readonly IProcessingDependencyHashIndex dependencyHashIndex;
    private readonly IProcessingFingerprintService fingerprintService;
    private readonly IRecordedSessionProcessingOptionCache processingOptionCache;
    private readonly IRecordedSessionDerivationWindowCache derivationWindowCache;
    private readonly IRecordedSessionProjectionScheduler scheduler;
    private readonly SourceCache<RecordedSessionSummary, Guid> summaries = new(summary => summary.Id);
    private readonly Dictionary<Guid, Guid?> sessionToSetup = [];
    private readonly Dictionary<Guid, HashSet<Guid>> setupToSessions = [];
    private readonly Dictionary<Guid, RecordedSessionDomainSnapshot> domains = [];
    private readonly Dictionary<Guid, ReplaySubject<RecordedSessionDomainSnapshot>> watches = [];
    private readonly CompositeDisposable subscriptions = [];
    private readonly System.Threading.Lock stateGate = new();
    private readonly HashSet<Guid> pendingRecomputeIds = [];
    private bool recomputeFlushScheduled;
    private bool disposed;

    public RecordedSessionProjection(
        ISessionStore sessionStore,
        ISetupStore setupStore,
        IBikeStore bikeStore,
        IRecordedSessionSourceStore sourceStore,
        IProcessingFingerprintService fingerprintService,
        IProcessingDependencyHashIndex dependencyHashIndex,
        IRecordedSessionProcessingOptionCache processingOptionCache,
        IRecordedSessionDerivationWindowCache derivationWindowCache,
        IUiThreadDispatcher uiThreadDispatcher)
        : this(
            sessionStore,
            setupStore,
            bikeStore,
            sourceStore,
            fingerprintService,
            dependencyHashIndex,
            processingOptionCache,
            derivationWindowCache,
            new UiThreadRecordedSessionProjectionScheduler(uiThreadDispatcher))
    {
    }

    internal RecordedSessionProjection(
        ISessionStore sessionStore,
        ISetupStore setupStore,
        IBikeStore bikeStore,
        IRecordedSessionSourceStore sourceStore,
        IProcessingFingerprintService fingerprintService,
        IProcessingDependencyHashIndex dependencyHashIndex,
        IRecordedSessionProcessingOptionCache processingOptionCache,
        IRecordedSessionDerivationWindowCache derivationWindowCache,
        IRecordedSessionProjectionScheduler scheduler)
    {
        this.sessionStore = sessionStore;
        this.setupStore = setupStore;
        this.bikeStore = bikeStore;
        this.sourceStore = sourceStore;
        this.dependencyHashIndex = dependencyHashIndex;
        this.fingerprintService = fingerprintService;
        this.processingOptionCache = processingOptionCache;
        this.derivationWindowCache = derivationWindowCache;
        this.scheduler = scheduler;

        subscriptions.Add(sessionStore.Connect().Subscribe(ApplySessionChanges));
        subscriptions.Add(setupStore.Connect().Subscribe(ApplySetupChanges));
        subscriptions.Add(sourceStore.Connect().Subscribe(ApplySourceChanges));
        subscriptions.Add(dependencyHashIndex.Connect().Subscribe(ApplyDependencyHashChanges));

        // Preference->projection edge: a processing-option change re-evaluates the
        // affected session so EvaluateState re-runs with the new option.
        subscriptions.Add(processingOptionCache.OptionChanged.Subscribe(QueueRecompute));
        subscriptions.Add(derivationWindowCache.WindowChanged.Subscribe(QueueRecompute));
    }

    public IObservable<IChangeSet<RecordedSessionSummary, Guid>> ConnectSessions() => summaries.Connect();

    public IObservable<RecordedSessionDomainSnapshot> WatchSession(Guid sessionId)
    {
        ReplaySubject<RecordedSessionDomainSnapshot> watch;
        RecordedSessionDomainSnapshot? domain = null;
        var created = false;
        lock (stateGate)
        {
            if (disposed)
            {
                return Observable.Empty<RecordedSessionDomainSnapshot>();
            }

            (watch, created) = GetWatchLocked(sessionId);
            if (created)
            {
                domains.TryGetValue(sessionId, out domain);
            }
        }

        if (domain is not null)
        {
            watch.OnNext(domain);
        }

        return watch.AsObservable();
    }

    public void Dispose()
    {
        ReplaySubject<RecordedSessionDomainSnapshot>[] completedWatches;
        lock (stateGate)
        {
            disposed = true;
            pendingRecomputeIds.Clear();
            sessionToSetup.Clear();
            setupToSessions.Clear();
            completedWatches = watches.Values.ToArray();
            watches.Clear();
        }

        subscriptions.Dispose();
        summaries.Dispose();
        CompleteWatches(completedWatches);
    }

    private void ApplySessionChanges(IChangeSet<SessionSnapshot, Guid> changes)
    {
        var affected = new HashSet<Guid>();
        var completedWatches = new List<ReplaySubject<RecordedSessionDomainSnapshot>>();
        var replacedKeys = FindKeysRemovedAndReadded(changes);
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            foreach (var change in changes)
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    case ChangeReason.Update:
                    case ChangeReason.Refresh:
                        UpdateSessionSetupIndexLocked(change.Key, change.Current.SetupId);
                        affected.Add(change.Key);
                        break;
                    case ChangeReason.Remove:
                        if (replacedKeys.Contains(change.Key))
                        {
                            break;
                        }

                        RemoveSessionSetupIndexLocked(change.Key);
                        domains.Remove(change.Key);
                        summaries.RemoveKey(change.Key);
                        RemovePendingRecomputeLocked(change.Key);
                        if (watches.Remove(change.Key, out var watch))
                        {
                            completedWatches.Add(watch);
                        }
                        break;
                    case ChangeReason.Moved:
                        break;
                }
            }
        }

        CompleteWatches(completedWatches);
        QueueRecompute(affected);
    }

    private static HashSet<Guid> FindKeysRemovedAndReadded(IChangeSet<SessionSnapshot, Guid> changes)
    {
        var removed = new HashSet<Guid>();
        var added = new HashSet<Guid>();

        foreach (var change in changes)
        {
            switch (change.Reason)
            {
                case ChangeReason.Add:
                case ChangeReason.Update:
                case ChangeReason.Refresh:
                    added.Add(change.Key);
                    break;
                case ChangeReason.Remove:
                    removed.Add(change.Key);
                    break;
                case ChangeReason.Moved:
                    break;
            }
        }

        removed.IntersectWith(added);
        return removed;
    }

    private void ApplySetupChanges(IChangeSet<SetupSnapshot, Guid> changes)
    {
        var affectedAddsAndRemoves = new HashSet<Guid>();
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            foreach (var change in changes)
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                        affectedAddsAndRemoves.Add(change.Key);
                        break;
                    case ChangeReason.Update:
                    case ChangeReason.Refresh:
                        break;
                    case ChangeReason.Remove:
                        affectedAddsAndRemoves.Add(change.Key);
                        break;
                    case ChangeReason.Moved:
                        break;
                }
            }
        }

        if (affectedAddsAndRemoves.Count > 0)
        {
            QueueRecomputeForSetups(affectedAddsAndRemoves);
        }
    }

    private void ApplyDependencyHashChanges(ProcessingDependencyHashChange change) =>
        QueueRecomputeForSetups([change.SetupId]);

    private void ApplySourceChanges(IChangeSet<RecordedSessionSourceSnapshot, Guid> changes)
    {
        var sourceIds = new HashSet<Guid>();
        var affected = new HashSet<Guid>();
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            foreach (var change in changes)
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    case ChangeReason.Update:
                    case ChangeReason.Refresh:
                    case ChangeReason.Remove:
                        sourceIds.Add(change.Key);
                        break;
                    case ChangeReason.Moved:
                        break;
                }
            }
        }

        foreach (var sourceId in sourceIds)
        {
            affected.Add(sourceId);
            foreach (var sessionId in derivationWindowCache.GetSessionIdsReferencingSource(sourceId))
            {
                affected.Add(sessionId);
            }
        }

        QueueRecompute(affected);
    }

    private void QueueRecomputeForSetups(IEnumerable<Guid> setupIds)
    {
        List<Guid> sessionIds = [];
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            foreach (var setupId in setupIds.Distinct())
            {
                if (setupToSessions.TryGetValue(setupId, out var setupSessions))
                {
                    sessionIds.AddRange(setupSessions);
                }
            }
        }

        QueueRecompute(sessionIds);
    }

    public void QueueRecompute(Guid sessionId) => QueueRecompute([sessionId]);

    private void QueueRecompute(IEnumerable<Guid> sessionIds)
    {
        var shouldSchedule = false;
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            foreach (var sessionId in sessionIds)
            {
                pendingRecomputeIds.Add(sessionId);
            }

            if (pendingRecomputeIds.Count > 0 && !recomputeFlushScheduled)
            {
                recomputeFlushScheduled = true;
                shouldSchedule = true;
            }
        }

        if (shouldSchedule)
        {
            ScheduleRecomputeFlush();
        }
    }

    private void RemovePendingRecomputeLocked(Guid sessionId) => pendingRecomputeIds.Remove(sessionId);

    private void UpdateSessionSetupIndexLocked(Guid sessionId, Guid? setupId)
    {
        if (sessionToSetup.TryGetValue(sessionId, out var previousSetupId))
        {
            if (previousSetupId == setupId)
            {
                return;
            }

            RemoveSessionFromSetupIndexLocked(sessionId, previousSetupId);
        }

        sessionToSetup[sessionId] = setupId;
        if (setupId is not { } concreteSetupId)
        {
            return;
        }

        if (!setupToSessions.TryGetValue(concreteSetupId, out var setupSessions))
        {
            setupSessions = [];
            setupToSessions[concreteSetupId] = setupSessions;
        }

        setupSessions.Add(sessionId);
    }

    private void RemoveSessionSetupIndexLocked(Guid sessionId)
    {
        if (!sessionToSetup.Remove(sessionId, out var setupId))
        {
            return;
        }

        RemoveSessionFromSetupIndexLocked(sessionId, setupId);
    }

    private void RemoveSessionFromSetupIndexLocked(Guid sessionId, Guid? setupId)
    {
        if (setupId is not { } concreteSetupId ||
            !setupToSessions.TryGetValue(concreteSetupId, out var setupSessions))
        {
            return;
        }

        setupSessions.Remove(sessionId);
        if (setupSessions.Count == 0)
        {
            setupToSessions.Remove(concreteSetupId);
        }
    }

    private void ScheduleRecomputeFlush() => scheduler.Post(FlushPendingRecomputes);

    private void FlushPendingRecomputes()
    {
        Guid[] sessionIds;
        lock (stateGate)
        {
            if (disposed)
            {
                pendingRecomputeIds.Clear();
                recomputeFlushScheduled = false;
                return;
            }

            sessionIds = pendingRecomputeIds.ToArray();
            pendingRecomputeIds.Clear();
        }

        Recompute(sessionIds);

        var shouldSchedule = false;
        lock (stateGate)
        {
            if (disposed)
            {
                pendingRecomputeIds.Clear();
                recomputeFlushScheduled = false;
                return;
            }

            if (pendingRecomputeIds.Count == 0)
            {
                recomputeFlushScheduled = false;
                return;
            }

            shouldSchedule = true;
        }

        if (shouldSchedule)
        {
            ScheduleRecomputeFlush();
        }
    }

    private void Recompute(IEnumerable<Guid> sessionIds)
    {
        var emissions = new List<(ReplaySubject<RecordedSessionDomainSnapshot> Watch, RecordedSessionDomainSnapshot Domain)>();
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            foreach (var sessionId in sessionIds)
            {
                var session = sessionStore.Get(sessionId);
                if (session is null)
                {
                    continue;
                }

                var setup = session.SetupId is { } setupId
                    ? setupStore.Get(setupId)
                    : null;
                var bike = setup is null
                    ? null
                    : bikeStore.Get(setup.BikeId);
                var window = derivationWindowCache.Get(session.Id);
                var source = sourceStore.Get(
                    RecordedSessionDerivationResolver.GetEffectiveSourceSessionId(session.Id, window));

                var previous = domains.GetValueOrDefault(session.Id);
                var initial = previous is null;
                var dependencyHash = setup is null ? null : dependencyHashIndex.GetForSetup(setup.Id);
                var changeKind = initial
                    ? DerivedChangeKind.Initial
                    : ComputeChangeKind(
                        previous!,
                        session,
                        setup,
                        bike,
                        source,
                        dependencyHash,
                        window);

                var domain = RecordedSessionDomainSnapshotFactory.Create(
                    session,
                    setup,
                    bike,
                    source,
                    fingerprintService,
                    dependencyHash,
                    processingOptionCache.Get(session.Id),
                    window,
                    changeKind);
                domains[session.Id] = domain;
                summaries.AddOrUpdate(new RecordedSessionSummary(
                    session.Id,
                    session.Updated,
                    session.Name,
                    session.Description,
                    session.Timestamp,
                    session.HasProcessedData,
                    domain.Staleness,
                    session.DurationSeconds,
                    session.DistanceMeters,
                    session.AscentMeters,
                    session.DescentMeters));

                if (watches.TryGetValue(session.Id, out var watch))
                {
                    emissions.Add((watch, domain));
                }
            }
        }

        foreach (var (watch, domain) in emissions)
        {
            watch.OnNext(domain);
        }
    }

    private (ReplaySubject<RecordedSessionDomainSnapshot> Watch, bool Created) GetWatchLocked(Guid sessionId)
    {
        if (watches.TryGetValue(sessionId, out var watch))
        {
            return (watch, false);
        }

        watch = new ReplaySubject<RecordedSessionDomainSnapshot>(1);
        watches[sessionId] = watch;
        return (watch, true);
    }

    private static void CompleteWatches(IEnumerable<ReplaySubject<RecordedSessionDomainSnapshot>> completedWatches)
    {
        foreach (var watch in completedWatches)
        {
            watch.OnCompleted();
        }
    }

    private static DerivedChangeKind ComputeChangeKind(
        RecordedSessionDomainSnapshot previous,
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        string? dependencyHash,
        RecordedSessionDerivationWindow? window)
    {
        var changeKind = DerivedChangeKind.None;

        if (SessionMetadataChanged(previous.Session, session))
        {
            changeKind |= DerivedChangeKind.SessionMetadataChanged;
        }

        if (SessionTrackChanged(previous.Session, session))
        {
            changeKind |= DerivedChangeKind.DerivedTrackChanged;
        }

        if (previous.Session.HasProcessedData != session.HasProcessedData)
        {
            changeKind |= DerivedChangeKind.ProcessedDataAvailabilityChanged;
        }

        if (!SourceEquals(previous.Source, source))
        {
            changeKind |= DerivedChangeKind.SourceAvailabilityChanged;
        }

        if (!string.Equals(previous.DependencyHash, dependencyHash, StringComparison.Ordinal))
        {
            changeKind |= DerivedChangeKind.DependencyChanged;
        }

        if (!string.Equals(previous.Session.ProcessingFingerprintJson, session.ProcessingFingerprintJson, StringComparison.Ordinal))
        {
            changeKind |= DerivedChangeKind.FingerprintChanged;
        }

        if (previous.DerivationWindow != window)
        {
            changeKind |= DerivedChangeKind.FingerprintChanged;
        }

        return changeKind;
    }

    private static bool SessionTrackChanged(SessionSnapshot previous, SessionSnapshot current) =>
        previous.FullTrackId != current.FullTrackId ||
        previous.GpsOffsetSeconds != current.GpsOffsetSeconds;

    private static bool SessionMetadataChanged(SessionSnapshot previous, SessionSnapshot current) =>
        previous.Name != current.Name ||
        previous.Description != current.Description ||
        previous.SetupId != current.SetupId ||
        previous.Timestamp != current.Timestamp ||
        previous.FrontSpringRate != current.FrontSpringRate ||
        previous.FrontHighSpeedCompression != current.FrontHighSpeedCompression ||
        previous.FrontLowSpeedCompression != current.FrontLowSpeedCompression ||
        previous.FrontLowSpeedRebound != current.FrontLowSpeedRebound ||
        previous.FrontHighSpeedRebound != current.FrontHighSpeedRebound ||
        previous.RearSpringRate != current.RearSpringRate ||
        previous.RearHighSpeedCompression != current.RearHighSpeedCompression ||
        previous.RearLowSpeedCompression != current.RearLowSpeedCompression ||
        previous.RearLowSpeedRebound != current.RearLowSpeedRebound ||
        previous.RearHighSpeedRebound != current.RearHighSpeedRebound;

    private static bool SourceEquals(RecordedSessionSourceSnapshot? previous, RecordedSessionSourceSnapshot? current) =>
        previous?.SessionId == current?.SessionId &&
        previous?.SourceKind == current?.SourceKind &&
        previous?.SourceName == current?.SourceName &&
        previous?.SchemaVersion == current?.SchemaVersion &&
        previous?.SourceHash == current?.SourceHash;

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
