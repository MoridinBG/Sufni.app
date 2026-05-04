using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Avalonia.Threading;
using DynamicData;
using Sufni.App.Stores;

namespace Sufni.App.SessionGraph;

public sealed class RecordedSessionGraph : IRecordedSessionGraph, IDisposable
{
    private readonly IBikeStore bikeStore;
    private readonly IProcessingFingerprintService fingerprintService;
    private readonly SourceCache<RecordedSessionSummary, Guid> summaries = new(summary => summary.Id);
    private readonly Dictionary<Guid, SessionSnapshot> sessions = [];
    private readonly Dictionary<Guid, SetupSnapshot> setups = [];
    private readonly Dictionary<Guid, BikeSnapshot> bikes = [];
    private readonly Dictionary<Guid, RecordedSessionSourceSnapshot> sources = [];
    private readonly Dictionary<Guid, RecordedSessionDomainSnapshot> domains = [];
    private readonly Dictionary<Guid, ReplaySubject<RecordedSessionDomainSnapshot>> watches = [];
    private readonly CompositeDisposable subscriptions = [];
    private readonly object pendingRecomputeGate = new();
    private readonly HashSet<Guid> pendingRecomputeIds = [];
    private bool recomputeFlushScheduled;
    private bool disposed;

    public RecordedSessionGraph(
        ISessionStore sessionStore,
        ISetupStore setupStore,
        IBikeStore bikeStore,
        IRecordedSessionSourceStore sourceStore,
        IProcessingFingerprintService fingerprintService)
    {
        this.bikeStore = bikeStore;
        this.fingerprintService = fingerprintService;

        subscriptions.Add(sessionStore.Connect().Subscribe(ApplySessionChanges));
        subscriptions.Add(setupStore.Connect().Subscribe(ApplySetupChanges));
        subscriptions.Add(bikeStore.Connect().Subscribe(ApplyBikeChanges));
        subscriptions.Add(sourceStore.Connect().Subscribe(ApplySourceChanges));
    }

    public IObservable<IChangeSet<RecordedSessionSummary, Guid>> ConnectSessions() => summaries.Connect();

    public IObservable<RecordedSessionDomainSnapshot> WatchSession(Guid sessionId)
    {
        var (watch, created) = GetWatch(sessionId);
        if (created && domains.TryGetValue(sessionId, out var domain))
        {
            watch.OnNext(domain);
        }

        return watch.AsObservable();
    }

    public void Dispose()
    {
        lock (pendingRecomputeGate)
        {
            disposed = true;
            pendingRecomputeIds.Clear();
        }

        subscriptions.Dispose();
        summaries.Dispose();
        foreach (var watch in watches.Values)
        {
            watch.OnCompleted();
            watch.Dispose();
        }
    }

    private void ApplySessionChanges(IChangeSet<SessionSnapshot, Guid> changes)
    {
        var affected = new HashSet<Guid>();
        foreach (var change in changes)
        {
            switch (change.Reason)
            {
                case ChangeReason.Add:
                case ChangeReason.Update:
                case ChangeReason.Refresh:
                    sessions[change.Key] = change.Current;
                    affected.Add(change.Key);
                    break;
                case ChangeReason.Remove:
                    sessions.Remove(change.Key);
                    domains.Remove(change.Key);
                    summaries.RemoveKey(change.Key);
                    RemovePendingRecompute(change.Key);
                    break;
                case ChangeReason.Moved:
                    break;
            }
        }

        QueueRecompute(affected);
    }

    private void ApplySetupChanges(IChangeSet<SetupSnapshot, Guid> changes)
    {
        var changed = false;
        foreach (var change in changes)
        {
            switch (change.Reason)
            {
                case ChangeReason.Add:
                case ChangeReason.Update:
                case ChangeReason.Refresh:
                    setups[change.Key] = change.Current;
                    changed = true;
                    break;
                case ChangeReason.Remove:
                    setups.Remove(change.Key);
                    changed = true;
                    break;
                case ChangeReason.Moved:
                    break;
            }
        }

        if (changed)
        {
            QueueRecomputeAll();
        }
    }

    private void ApplyBikeChanges(IChangeSet<BikeSnapshot, Guid> changes)
    {
        var changed = false;
        foreach (var change in changes)
        {
            switch (change.Reason)
            {
                case ChangeReason.Add:
                case ChangeReason.Update:
                case ChangeReason.Refresh:
                    bikes[change.Key] = change.Current;
                    changed = true;
                    break;
                case ChangeReason.Remove:
                    bikes.Remove(change.Key);
                    changed = true;
                    break;
                case ChangeReason.Moved:
                    break;
            }
        }

        if (changed)
        {
            QueueRecomputeAll();
        }
    }

    private void ApplySourceChanges(IChangeSet<RecordedSessionSourceSnapshot, Guid> changes)
    {
        var affected = new HashSet<Guid>();
        foreach (var change in changes)
        {
            switch (change.Reason)
            {
                case ChangeReason.Add:
                case ChangeReason.Update:
                case ChangeReason.Refresh:
                    sources[change.Key] = change.Current;
                    affected.Add(change.Key);
                    break;
                case ChangeReason.Remove:
                    sources.Remove(change.Key);
                    affected.Add(change.Key);
                    break;
                case ChangeReason.Moved:
                    break;
            }
        }

        QueueRecompute(affected);
    }

    private void QueueRecomputeAll() => QueueRecompute(sessions.Keys.ToArray());

    private void QueueRecompute(IEnumerable<Guid> sessionIds)
    {
        var shouldSchedule = false;
        var context = SynchronizationContext.Current;
        lock (pendingRecomputeGate)
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
            ScheduleRecomputeFlush(context);
        }
    }

    private void RemovePendingRecompute(Guid sessionId)
    {
        lock (pendingRecomputeGate)
        {
            pendingRecomputeIds.Remove(sessionId);
        }
    }

    private void ScheduleRecomputeFlush(SynchronizationContext? context)
    {
        if (context is not null)
        {
            context.Post(static state => ((RecordedSessionGraph)state!).FlushPendingRecomputes(), this);
            return;
        }

        Dispatcher.UIThread.Post(
            static state => ((RecordedSessionGraph)state!).FlushPendingRecomputes(),
            this,
            DispatcherPriority.Background);
    }

    private void FlushPendingRecomputes()
    {
        Guid[] sessionIds;
        lock (pendingRecomputeGate)
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

        SynchronizationContext? context = null;
        var shouldSchedule = false;
        lock (pendingRecomputeGate)
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

            context = SynchronizationContext.Current;
            shouldSchedule = true;
        }

        if (shouldSchedule)
        {
            ScheduleRecomputeFlush(context);
        }
    }

    private void Recompute(IEnumerable<Guid> sessionIds)
    {
        foreach (var sessionId in sessionIds)
        {
            if (!sessions.TryGetValue(sessionId, out var session))
            {
                continue;
            }

            setups.TryGetValue(session.SetupId ?? Guid.Empty, out var setup);
            var bike = setup is null
                ? null
                : bikes.GetValueOrDefault(setup.BikeId);
            sources.TryGetValue(session.Id, out var source);

            var previous = domains.GetValueOrDefault(session.Id);
            var initial = previous is null;
            var changeKind = initial
                ? DerivedChangeKind.Initial
                : ComputeChangeKind(previous!, session, setup, bike, source);

            var domain = RecordedSessionDomainSnapshotFactory.Create(
                session,
                setup,
                bike,
                source,
                fingerprintService,
                changeKind);
            domains[session.Id] = domain;
            summaries.AddOrUpdate(new RecordedSessionSummary(
                session.Id,
                session.Name,
                session.Description,
                session.Timestamp,
                session.HasProcessedData,
                domain.Staleness));

            if (watches.TryGetValue(session.Id, out var watch))
            {
                watch.OnNext(domain);
            }
        }
    }

    private (ReplaySubject<RecordedSessionDomainSnapshot> Watch, bool Created) GetWatch(Guid sessionId)
    {
        if (watches.TryGetValue(sessionId, out var watch))
        {
            return (watch, false);
        }

        watch = new ReplaySubject<RecordedSessionDomainSnapshot>(1);
        watches[sessionId] = watch;
        return (watch, true);
    }

    private static DerivedChangeKind ComputeChangeKind(
        RecordedSessionDomainSnapshot previous,
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source)
    {
        var changeKind = DerivedChangeKind.None;

        if (SessionMetadataChanged(previous.Session, session))
        {
            changeKind |= DerivedChangeKind.SessionMetadataChanged;
        }

        if (previous.Session.HasProcessedData != session.HasProcessedData)
        {
            changeKind |= DerivedChangeKind.ProcessedDataAvailabilityChanged;
        }

        if (!SourceEquals(previous.Source, source))
        {
            changeKind |= DerivedChangeKind.SourceAvailabilityChanged;
        }

        if (DependencyKey(previous.Setup, previous.Bike) != DependencyKey(setup, bike))
        {
            changeKind |= DerivedChangeKind.DependencyChanged;
        }

        if (!string.Equals(previous.Session.ProcessingFingerprintJson, session.ProcessingFingerprintJson, StringComparison.Ordinal))
        {
            changeKind |= DerivedChangeKind.FingerprintChanged;
        }

        return changeKind;
    }

    private static bool SessionMetadataChanged(SessionSnapshot previous, SessionSnapshot current) =>
        previous.Name != current.Name ||
        previous.Description != current.Description ||
        previous.SetupId != current.SetupId ||
        previous.Timestamp != current.Timestamp ||
        previous.FullTrackId != current.FullTrackId ||
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

    private static string? DependencyKey(SetupSnapshot? setup, BikeSnapshot? bike) =>
        setup is null || bike is null
            ? null
            : ProcessingDependencyHash.Compute(setup, bike);
}
