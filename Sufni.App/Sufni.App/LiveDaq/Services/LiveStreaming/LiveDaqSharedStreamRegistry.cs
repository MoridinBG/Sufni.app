using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;

using Sufni.App.LiveDaq.Stores;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

internal sealed class LiveDaqSharedStreamRegistry : ILiveDaqSharedStreamRegistry, IAsyncDisposable
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqSharedStreamRegistry>();

    private readonly ILiveDaqClientFactory liveDaqClientFactory;
    private readonly ILiveDaqCatalogService liveDaqCatalogService;
    private readonly System.Threading.Lock gate = new();
    private readonly IDisposable catalogSubscription;
    private readonly Channel<CatalogUpdate> catalogUpdates = Channel.CreateUnbounded<CatalogUpdate>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Task catalogLoop;

    private readonly ConcurrentDictionary<string, LiveDaqSharedStream> streams = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TransportLease> transports = new(StringComparer.OrdinalIgnoreCase);
    private readonly BehaviorSubject<IReadOnlyDictionary<string, DaqTransportHolder>> transportStates = new(
        new Dictionary<string, DaqTransportHolder>(StringComparer.OrdinalIgnoreCase));
    private IDisposable? browseLease;
    private volatile bool disposed;

    public LiveDaqSharedStreamRegistry(
        ILiveDaqClientFactory liveDaqClientFactory,
        ILiveDaqCatalogService liveDaqCatalogService)
    {
        this.liveDaqClientFactory = liveDaqClientFactory;
        this.liveDaqCatalogService = liveDaqCatalogService;
        catalogSubscription = liveDaqCatalogService.Observe().Subscribe(entries =>
        {
            // Discovery can notify while a browse lease is being released under the registry gate.
            if (!disposed) catalogUpdates.Writer.TryWrite(new CatalogUpdate([.. entries], [.. streams.Values]));
        });
        catalogLoop = Task.Run(ProcessCatalogUpdatesAsync);
    }

    public ILiveDaqSharedStream GetOrCreate(LiveDaqSnapshot snapshot)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (streams.TryGetValue(snapshot.IdentityKey, out var existing))
            {
                if (existing.CanBeReturnedFromRegistry())
                {
                    return existing;
                }

                streams.TryRemove(snapshot.IdentityKey, out _);
            }

            EnsureBrowseLeaseLocked();
            var stream = new LiveDaqSharedStream(snapshot, liveDaqClientFactory, EvictAsync,
                () => AcquireTransport(snapshot.IdentityKey, new DaqTransportHolder.Streaming()));
            streams.TryAdd(snapshot.IdentityKey, stream);
            return stream;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        catalogSubscription.Dispose();
        catalogUpdates.Writer.TryComplete();
        await catalogLoop.ConfigureAwait(false);

        foreach (var stream in streams.Values.ToArray())
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        streams.Clear();
        browseLease?.Dispose();
        browseLease = null;
        transportStates.Dispose();
    }

    public IObservable<DaqTransportHolder?> ObserveTransport(string identityKey) =>
        transportStates.Select(states => states.GetValueOrDefault(identityKey)).DistinctUntilChanged();

    public Task<DaqTransportAcquisition> AcquireManagementAsync(string identityKey, DaqManagementOperation operation) =>
        Task.FromResult(AcquireTransport(identityKey, new DaqTransportHolder.Management(operation)));

    public Task<DaqTransportAcquisition> AcquireImportAsync(string identityKey, Guid batchId) =>
        Task.FromResult(AcquireTransport(identityKey, new DaqTransportHolder.Import(batchId)));

    public Task<DaqTransportAcquisition> AcquireIdentifyAsync(string identityKey) =>
        Task.FromResult(AcquireTransport(identityKey, new DaqTransportHolder.Identify()));

    private DaqTransportAcquisition AcquireTransport(string identityKey, DaqTransportHolder holder)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (transports.TryGetValue(identityKey, out var held))
                return new DaqTransportAcquisition.Busy(held.Holder);
            EnsureBrowseLeaseLocked();
            var lease = new TransportLease(this, identityKey, holder);
            transports.Add(identityKey, lease);
            PublishTransportsLocked();
            return new DaqTransportAcquisition.Acquired(lease);
        }
    }

    private void ReleaseTransport(TransportLease lease)
    {
        lock (gate)
        {
            transports.Remove(lease.IdentityKey);
            if (disposed) return;
            PublishTransportsLocked();
            ReleaseUnusedBrowseLeaseLocked();
        }
    }

    private void PublishTransportsLocked() => transportStates.OnNext(
        transports.ToDictionary(pair => pair.Key, pair => pair.Value.Holder, StringComparer.OrdinalIgnoreCase));

    private void ReleaseUnusedBrowseLeaseLocked()
    {
        if (streams.Count != 0 || transports.Count != 0) return;
        browseLease?.Dispose();
        browseLease = null;
    }

    private sealed class TransportLease(LiveDaqSharedStreamRegistry owner, string identityKey, DaqTransportHolder holder) : IDisposable
    {
        private LiveDaqSharedStreamRegistry? owner = owner;
        public string IdentityKey { get; } = identityKey;
        public DaqTransportHolder Holder { get; } = holder;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.ReleaseTransport(this);
    }

    private sealed record CatalogUpdate(LiveDaqCatalogEntry[] Entries, LiveDaqSharedStream[] Streams);

    private async Task ProcessCatalogUpdatesAsync()
    {
        await foreach (var update in catalogUpdates.Reader.ReadAllAsync())
        {
            var entries = update.Entries.ToDictionary(entry => entry.IdentityKey, StringComparer.OrdinalIgnoreCase);
            foreach (var stream in update.Streams)
            {
                if (!stream.CanBeReturnedFromRegistry()) continue;
                try
                {
                    if (entries.TryGetValue(stream.IdentityKey, out var entry))
                    {
                        await stream.UpdateCatalogSnapshotAsync(new LiveDaqSnapshot(
                            entry.IdentityKey, entry.DisplayName, entry.BoardId, entry.Host, entry.Port,
                            true, null, null, entry.ProtocolVersion));
                    }
                    else
                    {
                        await stream.CloseAsync(LiveStreamClosedReason.Offline);
                    }
                }
                catch (ObjectDisposedException) { }
                catch (Exception exception)
                {
                    logger.Error(exception, "Failed to apply catalog change for DAQ {IdentityKey}", stream.IdentityKey);
                }
            }
        }
    }

    private Task EvictAsync(LiveDaqSharedStream stream)
    {
        lock (gate)
        {
            if (!streams.TryGetValue(stream.IdentityKey, out var current) || !ReferenceEquals(current, stream))
            {
                return Task.CompletedTask;
            }

            streams.TryRemove(stream.IdentityKey, out _);

            logger.Debug("Evicted shared live DAQ stream for {IdentityKey}", stream.IdentityKey);
            ReleaseUnusedBrowseLeaseLocked();
        }
        return Task.CompletedTask;
    }

    private void EnsureBrowseLeaseLocked()
    {
        browseLease ??= liveDaqCatalogService.AcquireBrowse();
    }

}
