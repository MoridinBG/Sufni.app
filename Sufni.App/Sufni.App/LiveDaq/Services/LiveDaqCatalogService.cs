using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Subjects;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shared.Common;
namespace Sufni.App.LiveDaq.Services;

// Normalizes raw discovery announcements into live-preview catalog entries and
// keeps endpoint-only rows available when TXT board identity is absent.
public sealed class LiveDaqCatalogService : ILiveDaqCatalogService, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqCatalogService>();

    private readonly IServiceDiscovery serviceDiscovery;
    private readonly IDaqBrowseOwner browseOwner;
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<string, LiveDaqCatalogEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly BehaviorSubject<IReadOnlyList<LiveDaqCatalogEntry>> entriesSubject = new([]);

    public LiveDaqCatalogService(
        [FromKeyedServices("daq")] IServiceDiscovery serviceDiscovery,
        IDaqBrowseOwner browseOwner)
    {
        this.serviceDiscovery = serviceDiscovery;
        this.browseOwner = browseOwner;

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
        var announcementKey = GetAnnouncementKey(e.Announcement, endpoint);

        if (!TryGetProtocolVersion(e.Announcement, out var protocolVersion))
        {
            logger.Verbose(
                "Ignoring live DAQ announcement at {Endpoint} because TXT live_proto is missing or unsupported",
                endpoint);
            return;
        }

        logger.Verbose(
            "Live DAQ found at {Endpoint} with host {Host}, port {Port}, protocol {ProtocolVersion}, and instance {InstanceName}",
            endpoint,
            host,
            e.Announcement.Port,
            protocolVersion,
            e.Announcement.InstanceName);

        lock (gate)
        {
            var boardId = TryGetBoardId(e.Announcement);
            var identityKey = boardId ?? CreateEndpointIdentity(protocolVersion, host, e.Announcement.Port);
            var displayName = boardId ?? identityKey;
            entries[announcementKey] = new LiveDaqCatalogEntry(
                identityKey,
                displayName,
                boardId,
                host,
                e.Announcement.Port,
                protocolVersion);
            PublishSnapshotLocked();
        }
    }

    private void OnServiceRemoved(object? sender, ServiceAnnouncementEventArgs e)
    {
        var address = NormalizeAddress(e.Announcement.Address);
        var host = FormatHost(address);
        var endpoint = $"{host}:{e.Announcement.Port}";
        var announcementKey = GetAnnouncementKey(e.Announcement, endpoint);

        logger.Verbose("Live DAQ lost at {Endpoint}", endpoint);

        lock (gate)
        {
            if (entries.Remove(announcementKey))
            {
                PublishSnapshotLocked();
            }
        }
    }

    private void PublishSnapshotLocked()
    {
        var snapshot = entries
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Value)
            .GroupBy(entry => entry.IdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        logger.Debug("Published live DAQ catalog snapshot with {EntryCount} entries", snapshot.Length);
        entriesSubject.OnNext(snapshot);
    }

    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static string FormatHost(IPAddress address) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]"
            : address.ToString();

    private static string GetAnnouncementKey(ServiceAnnouncement announcement, string endpoint) =>
        string.IsNullOrWhiteSpace(announcement.InstanceName)
            ? endpoint
            : announcement.InstanceName;

    private static bool TryGetProtocolVersion(ServiceAnnouncement announcement, out LiveProtocolVersion protocolVersion)
    {
        protocolVersion = default;
        if (!TryGetTxtValue(announcement, "live_proto", out var rawProtocol))
        {
            return false;
        }

        protocolVersion = rawProtocol switch
        {
            "2" => LiveProtocolVersion.V2,
            "3" => LiveProtocolVersion.V3,
            _ => default,
        };
        return protocolVersion is LiveProtocolVersion.V2 or LiveProtocolVersion.V3;
    }

    private static string? TryGetBoardId(ServiceAnnouncement announcement)
    {
        if (!TryGetTxtValue(announcement, "bid", out var bid) ||
            bid.Length != 16 ||
            !bid.All(Uri.IsHexDigit))
        {
            return null;
        }

        try
        {
            return UuidUtil.CreateDeviceUuid(bid).ToString();
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryGetTxtValue(ServiceAnnouncement announcement, string key, out string value)
    {
        foreach (var record in announcement.TxtRecords)
        {
            if (string.Equals(record.Key, key, StringComparison.Ordinal))
            {
                value = record.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string CreateEndpointIdentity(LiveProtocolVersion protocolVersion, string host, int port) =>
        $"{ProtocolPrefix(protocolVersion)}:{host}:{port}";

    private static string ProtocolPrefix(LiveProtocolVersion protocolVersion) =>
        protocolVersion switch
        {
            LiveProtocolVersion.V2 => "v2",
            LiveProtocolVersion.V3 => "v3",
            _ => "unknown",
        };
}
