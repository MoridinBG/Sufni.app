using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Subjects;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.Services;

// Normalizes raw discovery announcements into live-preview catalog entries and
// keeps endpoint-only rows available when board inspection fails.
public sealed class LiveDaqCatalogService : ILiveDaqCatalogService, IDisposable
{
    private readonly IServiceDiscovery serviceDiscovery;
    private readonly ILiveDaqBrowseOwner browseOwner;
    private readonly ILiveDaqBoardIdInspector boardIdInspector;
    private readonly object gate = new();
    private readonly HashSet<string> announcedEndpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LiveDaqCatalogEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly BehaviorSubject<IReadOnlyList<LiveDaqCatalogEntry>> entriesSubject = new([]);

    public LiveDaqCatalogService(
        [FromKeyedServices("gosst")] IServiceDiscovery serviceDiscovery,
        ILiveDaqBrowseOwner browseOwner,
        ILiveDaqBoardIdInspector boardIdInspector)
    {
        this.serviceDiscovery = serviceDiscovery;
        this.browseOwner = browseOwner;
        this.boardIdInspector = boardIdInspector;

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

        _ = InspectAndUpsertAsync(address, e.Announcement.Port, host, endpoint);
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

    private async System.Threading.Tasks.Task InspectAndUpsertAsync(IPAddress address, int port, string host, string endpoint)
    {
        string? boardId = null;
        try
        {
            boardId = (await boardIdInspector.InspectAsync(address, port).ConfigureAwait(false))?.ToString();
        }
        catch
        {
            // Discovery should still surface endpoint-only entries when
            // identity inspection fails.
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