using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Subjects;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.Services;

public sealed class LiveDaqCatalogService : ILiveDaqCatalogService, IDisposable
{
    private readonly IServiceDiscovery serviceDiscovery;
    private readonly ILiveDaqBrowseOwner browseOwner;
    private readonly ILiveDaqBoardIdProbe boardIdProbe;
    private readonly object gate = new();
    private readonly HashSet<string> announcedEndpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LiveDaqCatalogEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly BehaviorSubject<IReadOnlyList<LiveDaqCatalogEntry>> entriesSubject = new([]);

    public LiveDaqCatalogService(
        [FromKeyedServices("gosst")] IServiceDiscovery serviceDiscovery,
        ILiveDaqBrowseOwner browseOwner,
        ILiveDaqBoardIdProbe boardIdProbe)
    {
        this.serviceDiscovery = serviceDiscovery;
        this.browseOwner = browseOwner;
        this.boardIdProbe = boardIdProbe;

        serviceDiscovery.ServiceAdded += OnServiceAdded;
        serviceDiscovery.ServiceRemoved += OnServiceRemoved;
    }

    public IDisposable AcquireBrowse() => browseOwner.AcquireBrowse();

    public IObservable<IReadOnlyList<LiveDaqCatalogEntry>> Observe() => entriesSubject;

    public void Dispose()
    {
        serviceDiscovery.ServiceAdded -= OnServiceAdded;
        serviceDiscovery.ServiceRemoved -= OnServiceRemoved;
        entriesSubject.Dispose();
    }

    private void OnServiceAdded(object? sender, ServiceAnnouncementEventArgs e)
    {
        var address = NormalizeAddress(e.Announcement.Address);
        var host = FormatHost(address);
        var endpoint = $"{host}:{e.Announcement.Port}";

        lock (gate)
        {
            announcedEndpoints.Add(endpoint);
        }

        _ = ProbeAndUpsertAsync(address, e.Announcement.Port, host, endpoint);
    }

    private void OnServiceRemoved(object? sender, ServiceAnnouncementEventArgs e)
    {
        var address = NormalizeAddress(e.Announcement.Address);
        var host = FormatHost(address);
        var endpoint = $"{host}:{e.Announcement.Port}";

        lock (gate)
        {
            announcedEndpoints.Remove(endpoint);
            if (entries.Remove(endpoint))
            {
                PublishSnapshotLocked();
            }
        }
    }

    private async System.Threading.Tasks.Task ProbeAndUpsertAsync(IPAddress address, int port, string host, string endpoint)
    {
        string? boardId = null;
        try
        {
            boardId = (await boardIdProbe.ProbeAsync(address, port).ConfigureAwait(false))?.ToString();
        }
        catch
        {
            // Discovery should still surface endpoint-only entries when
            // identity probing fails.
        }

        lock (gate)
        {
            if (!announcedEndpoints.Contains(endpoint))
            {
                return;
            }

            var identityKey = boardId ?? endpoint;
            var displayName = boardId ?? endpoint;
            entries[endpoint] = new LiveDaqCatalogEntry(identityKey, displayName, boardId, host, port);
            PublishSnapshotLocked();
        }
    }

    private void PublishSnapshotLocked()
    {
        entriesSubject.OnNext(entries.Values
            .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
    }

    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static string FormatHost(IPAddress address) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]"
            : address.ToString();
}