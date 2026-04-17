using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.Services;
using Sufni.App.Stores;
using Serilog;

namespace Sufni.App.Services.LiveStreaming;

internal sealed class LiveDaqSharedStreamRegistry : ILiveDaqSharedStreamRegistry, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqSharedStreamRegistry>();

    private readonly ILiveDaqClientFactory liveDaqClientFactory;
    private readonly ILiveDaqCatalogService liveDaqCatalogService;
    private readonly object gate = new();
    private readonly IDisposable catalogSubscription;

    private readonly Dictionary<string, LiveDaqSharedStream> streams = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? browseLease;

    public LiveDaqSharedStreamRegistry(
        ILiveDaqClientFactory liveDaqClientFactory,
        ILiveDaqCatalogService liveDaqCatalogService)
    {
        this.liveDaqClientFactory = liveDaqClientFactory;
        this.liveDaqCatalogService = liveDaqCatalogService;
        catalogSubscription = liveDaqCatalogService.Observe().Subscribe(entries => _ = HandleCatalogEntriesAsync(entries));
    }

    public ILiveDaqSharedStream GetOrCreate(LiveDaqSnapshot snapshot)
    {
        lock (gate)
        {
            if (streams.TryGetValue(snapshot.IdentityKey, out var existing))
            {
                _ = existing.UpdateCatalogSnapshotAsync(snapshot);
                return existing;
            }

            EnsureBrowseLeaseLocked();
            var stream = new LiveDaqSharedStream(snapshot, liveDaqClientFactory, EvictAsync);
            streams.Add(snapshot.IdentityKey, stream);
            return stream;
        }
    }

    public void Dispose()
    {
        catalogSubscription.Dispose();
        browseLease?.Dispose();
        browseLease = null;

        foreach (var stream in streams.Values.ToArray())
        {
            stream.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        streams.Clear();
    }

    private async System.Threading.Tasks.Task HandleCatalogEntriesAsync(IReadOnlyList<LiveDaqCatalogEntry> entries)
    {
        List<(LiveDaqSharedStream Stream, LiveDaqSnapshot Snapshot)> streamsToRefresh = [];
        List<LiveDaqSharedStream> streamsToClose = [];
        var catalogEntries = entries.ToDictionary(entry => entry.IdentityKey, StringComparer.OrdinalIgnoreCase);

        lock (gate)
        {
            foreach (var stream in streams.Values)
            {
                if (catalogEntries.TryGetValue(stream.IdentityKey, out var entry))
                {
                    streamsToRefresh.Add((
                        stream,
                        new LiveDaqSnapshot(
                            IdentityKey: entry.IdentityKey,
                            DisplayName: entry.DisplayName,
                            BoardId: entry.BoardId,
                            Host: entry.Host,
                            Port: entry.Port,
                            IsOnline: true,
                            SetupName: null,
                            BikeName: null)));
                    continue;
                }

                streamsToClose.Add(stream);
            }
        }

        foreach (var (stream, snapshot) in streamsToRefresh)
        {
            await stream.UpdateCatalogSnapshotAsync(snapshot);
        }

        foreach (var stream in streamsToClose)
        {
            await stream.CloseAsync("DAQ went offline.");
        }
    }

    private async System.Threading.Tasks.Task EvictAsync(LiveDaqSharedStream stream)
    {
        lock (gate)
        {
            if (!streams.Remove(stream.IdentityKey))
            {
                return;
            }

            logger.Debug("Evicted shared live DAQ stream for {IdentityKey}", stream.IdentityKey);
            if (streams.Count == 0)
            {
                browseLease?.Dispose();
                browseLease = null;
            }
        }
    }

    private void EnsureBrowseLeaseLocked()
    {
        browseLease ??= liveDaqCatalogService.AcquireBrowse();
    }
}