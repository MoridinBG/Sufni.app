using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;

using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Stores;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

public interface IProcessingDependencyHashIndex : IDisposable
{
    string? GetForSetup(Guid setupId);

    IObservable<ProcessingDependencyHashChange> Connect();
}

public sealed record ProcessingDependencyHashChange(Guid SetupId, string? PreviousHash, string? CurrentHash);

internal sealed class ProcessingDependencyHashIndex : IProcessingDependencyHashIndex
{
    private readonly System.Threading.Lock stateGate = new();
    private readonly ISetupStore setupStore;
    private readonly IBikeStore bikeStore;
    private readonly Dictionary<Guid, Guid> setupToBike = [];
    private readonly Dictionary<Guid, HashSet<Guid>> bikeToSetups = [];
    private readonly Dictionary<Guid, string> hashes = [];
    private readonly ISubject<ProcessingDependencyHashChange> changes =
        Subject.Synchronize(new Subject<ProcessingDependencyHashChange>());
    private readonly CompositeDisposable subscriptions = [];
    private bool disposed;

    public ProcessingDependencyHashIndex(ISetupStore setupStore, IBikeStore bikeStore)
    {
        this.setupStore = setupStore;
        this.bikeStore = bikeStore;

        subscriptions.Add(setupStore.Connect().Subscribe(ApplySetupChanges));
        subscriptions.Add(bikeStore.Connect().Subscribe(ApplyBikeChanges));
    }

    public string? GetForSetup(Guid setupId)
    {
        lock (stateGate)
        {
            return hashes.GetValueOrDefault(setupId);
        }
    }

    public IObservable<ProcessingDependencyHashChange> Connect() => changes.AsObservable();

    public void Dispose()
    {
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            setupToBike.Clear();
            bikeToSetups.Clear();
            hashes.Clear();
        }

        subscriptions.Dispose();
        changes.OnCompleted();
    }

    private void ApplySetupChanges(IChangeSet<SetupSnapshot, Guid> changeSet)
    {
        var affectedSetupIds = new HashSet<Guid>();
        var emitted = new List<ProcessingDependencyHashChange>();
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            foreach (var change in changeSet)
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    case ChangeReason.Update:
                    case ChangeReason.Refresh:
                        UpdateSetupBikeIndexLocked(change.Key, change.Current.BikeId);
                        affectedSetupIds.Add(change.Key);
                        break;
                    case ChangeReason.Remove:
                        RemoveSetupBikeIndexLocked(change.Key);
                        affectedSetupIds.Add(change.Key);
                        break;
                    case ChangeReason.Moved:
                        break;
                }
            }

            foreach (var setupId in affectedSetupIds)
            {
                AddIfChanged(emitted, RecomputeSetupHashLocked(setupId));
            }
        }

        Emit(emitted);
    }

    private void ApplyBikeChanges(IChangeSet<BikeSnapshot, Guid> changeSet)
    {
        var affectedBikeIds = new HashSet<Guid>();
        var emitted = new List<ProcessingDependencyHashChange>();
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            foreach (var change in changeSet)
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    case ChangeReason.Update:
                    case ChangeReason.Refresh:
                        affectedBikeIds.Add(change.Key);
                        break;
                    case ChangeReason.Remove:
                        affectedBikeIds.Add(change.Key);
                        break;
                    case ChangeReason.Moved:
                        break;
                }
            }

            foreach (var setupId in affectedBikeIds.SelectMany(SetupIdsForBikeLocked).Distinct())
            {
                AddIfChanged(emitted, RecomputeSetupHashLocked(setupId));
            }
        }

        Emit(emitted);
    }

    private IEnumerable<Guid> SetupIdsForBikeLocked(Guid bikeId) =>
        bikeToSetups.TryGetValue(bikeId, out var setupIds)
            ? setupIds.ToArray()
            : [];

    private ProcessingDependencyHashChange? RecomputeSetupHashLocked(Guid setupId)
    {
        var setup = setupStore.Get(setupId);
        var current = setup is not null &&
                      bikeStore.Get(setup.BikeId) is { } bike
            ? ProcessingDependencyHash.Compute(setup, bike)
            : null;

        hashes.TryGetValue(setupId, out var previous);
        if (StringComparer.Ordinal.Equals(previous, current))
        {
            return null;
        }

        if (current is null)
        {
            hashes.Remove(setupId);
        }
        else
        {
            hashes[setupId] = current;
        }

        return new ProcessingDependencyHashChange(setupId, previous, current);
    }

    private void UpdateSetupBikeIndexLocked(Guid setupId, Guid bikeId)
    {
        if (setupToBike.TryGetValue(setupId, out var previousBikeId))
        {
            if (previousBikeId == bikeId)
            {
                return;
            }

            RemoveSetupFromBikeIndexLocked(setupId, previousBikeId);
        }

        setupToBike[setupId] = bikeId;
        if (!bikeToSetups.TryGetValue(bikeId, out var setupIds))
        {
            setupIds = [];
            bikeToSetups[bikeId] = setupIds;
        }

        setupIds.Add(setupId);
    }

    private void RemoveSetupBikeIndexLocked(Guid setupId)
    {
        if (!setupToBike.Remove(setupId, out var bikeId))
        {
            return;
        }

        RemoveSetupFromBikeIndexLocked(setupId, bikeId);
    }

    private void RemoveSetupFromBikeIndexLocked(Guid setupId, Guid bikeId)
    {
        if (!bikeToSetups.TryGetValue(bikeId, out var setupIds))
        {
            return;
        }

        setupIds.Remove(setupId);
        if (setupIds.Count == 0)
        {
            bikeToSetups.Remove(bikeId);
        }
    }

    private static void AddIfChanged(
        List<ProcessingDependencyHashChange> emitted,
        ProcessingDependencyHashChange? change)
    {
        if (change is not null)
        {
            emitted.Add(change);
        }
    }

    private void Emit(IReadOnlyList<ProcessingDependencyHashChange> emitted)
    {
        if (emitted.Count == 0)
        {
            return;
        }

        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }
        }

        foreach (var change in emitted)
        {
            changes.OnNext(change);
        }
    }
}
