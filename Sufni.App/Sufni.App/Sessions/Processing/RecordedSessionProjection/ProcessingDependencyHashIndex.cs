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
    private readonly Dictionary<Guid, SetupSnapshot> setups = [];
    private readonly Dictionary<Guid, BikeSnapshot> bikes = [];
    private readonly Dictionary<Guid, string> hashes = [];
    private readonly Subject<ProcessingDependencyHashChange> changes = new();
    private readonly CompositeDisposable subscriptions = [];
    private bool disposed;

    public ProcessingDependencyHashIndex(ISetupStore setupStore, IBikeStore bikeStore)
    {
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
            setups.Clear();
            bikes.Clear();
            hashes.Clear();
        }

        subscriptions.Dispose();
        changes.OnCompleted();
        changes.Dispose();
    }

    private void ApplySetupChanges(IChangeSet<SetupSnapshot, Guid> changeSet)
    {
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
                        setups[change.Key] = change.Current;
                        AddIfChanged(emitted, RecomputeSetupHashLocked(change.Key));
                        break;
                    case ChangeReason.Remove:
                        setups.Remove(change.Key);
                        AddIfChanged(emitted, RemoveSetupHashLocked(change.Key));
                        break;
                    case ChangeReason.Moved:
                        break;
                }
            }
        }

        Emit(emitted);
    }

    private void ApplyBikeChanges(IChangeSet<BikeSnapshot, Guid> changeSet)
    {
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
                        bikes[change.Key] = change.Current;
                        foreach (var setupId in SetupIdsForBikeLocked(change.Key))
                        {
                            AddIfChanged(emitted, RecomputeSetupHashLocked(setupId));
                        }
                        break;
                    case ChangeReason.Remove:
                        bikes.Remove(change.Key);
                        foreach (var setupId in SetupIdsForBikeLocked(change.Key))
                        {
                            AddIfChanged(emitted, RecomputeSetupHashLocked(setupId));
                        }
                        break;
                    case ChangeReason.Moved:
                        break;
                }
            }
        }

        Emit(emitted);
    }

    private IEnumerable<Guid> SetupIdsForBikeLocked(Guid bikeId) =>
        setups.Values
            .Where(setup => setup.BikeId == bikeId)
            .Select(setup => setup.Id)
            .ToArray();

    private ProcessingDependencyHashChange? RecomputeSetupHashLocked(Guid setupId)
    {
        var current = setups.TryGetValue(setupId, out var setup) &&
                      bikes.TryGetValue(setup.BikeId, out var bike)
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

    private ProcessingDependencyHashChange? RemoveSetupHashLocked(Guid setupId)
    {
        if (!hashes.Remove(setupId, out var previous))
        {
            return null;
        }

        return new ProcessingDependencyHashChange(setupId, previous, CurrentHash: null);
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

    private void Emit(IEnumerable<ProcessingDependencyHashChange> emitted)
    {
        foreach (var change in emitted)
        {
            changes.OnNext(change);
        }
    }
}
