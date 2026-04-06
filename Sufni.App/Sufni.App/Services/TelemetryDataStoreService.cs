using System;
using System.Collections.Generic;
using Sufni.App.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using ServiceDiscovery;

namespace Sufni.App.Services;

internal class DriveInfoComparer : IEqualityComparer<DriveInfo>
{
    public bool Equals(DriveInfo? ds1, DriveInfo? ds2)
    {
        if (ReferenceEquals(ds1, ds2))
            return true;

        if (ds1 is null || ds2 is null)
            return false;

        if (!ds1.IsReady || !ds2.IsReady)
            return false;

        return ds1.VolumeLabel == ds2.VolumeLabel;
    }

    public int GetHashCode(DriveInfo ds) => ds.IsReady ? ds.VolumeLabel.GetHashCode() : 0;
}

internal class TelemetryDataStoreService : ITelemetryDataStoreService
{
    private const string ServiceType = "_gosst._tcp";
    private readonly IServiceDiscovery serviceDiscovery;
    private readonly DispatcherTimer massStorageScanTimer;

    public ObservableCollection<ITelemetryDataStore> DataStores { get; } = new();

    private void GetMassStorageDatastores()
    {
        var comparer = new DriveInfoComparer();
        var current = DriveInfo.GetDrives()
            .Where(drive => drive is { IsReady: true } &&
                            File.Exists($"{drive.RootDirectory}/BOARDID")).ToArray();

        var known = DataStores
            .Where(ds => ds is MassStorageTelemetryDataStore)
            .Select(ds => ((MassStorageTelemetryDataStore)ds).DriveInfo)
            .ToArray();
        var added = current.Except(known, comparer).ToArray();
        var removed = known.Except(current, comparer).ToArray();

        foreach (var drive in added)
        {
            DataStores.Add(new MassStorageTelemetryDataStore(drive));
        }

        foreach (var drive in removed)
        {
            var toRemove = DataStores
                .First(ds => ds is MassStorageTelemetryDataStore msds &&
                             comparer.Equals(msds.DriveInfo, drive));
            DataStores.Remove(toRemove);
        }
    }

    private void RemoveNetworkDataStore(ServiceAnnouncementEventArgs e)
    {
        var ipAddress = e.Announcement.Address;
        var port = e.Announcement.Port;
        var name = $"gosst://{ipAddress}:{port}";

        Dispatcher.UIThread.Post(() =>
        {
            var toRemove = DataStores.First(x => x.Name == name);
            DataStores.Remove(toRemove);
        });
    }

    private async Task AddNetworkDataStore(ServiceAnnouncementEventArgs e)
    {
        var ipAddress = e.Announcement.Address;
        var port = e.Announcement.Port;

        var ds = new NetworkTelemetryDataStore(ipAddress, port);
        await ds.Initialization;

        Dispatcher.UIThread.Post(() =>
        {
            DataStores.Add(ds);
        });
    }

    private List<StorageProviderTelemetryDataStore> GetRemovedStorageProviders()
    {
        var storageProviderDatastores = DataStores
            .OfType<StorageProviderTelemetryDataStore>()
            .ToArray();
        var toRemove = new List<StorageProviderTelemetryDataStore>();
        foreach (var ds in storageProviderDatastores)
        {
            if (!ds.IsAvailable())
            {
                toRemove.Add(ds);
            }
        }

        return toRemove;
    }

    public TelemetryDataStoreService([FromKeyedServices("gosst")] IServiceDiscovery serviceDiscovery)
    {
        this.serviceDiscovery = serviceDiscovery;

        serviceDiscovery.ServiceAdded += async (_, e) => await AddNetworkDataStore(e);
        serviceDiscovery.ServiceRemoved += (_, e) => RemoveNetworkDataStore(e);

        massStorageScanTimer = new DispatcherTimer(DispatcherPriority.Background);
        massStorageScanTimer.Interval = TimeSpan.FromSeconds(1);
        massStorageScanTimer.Tick += (_, _) =>
        {
            var removedStorageProviders = GetRemovedStorageProviders();
            foreach (var ds in removedStorageProviders)
            {
                DataStores.Remove(ds);
            }

            GetMassStorageDatastores();
        };
    }

    public void StartBrowse()
    {
        massStorageScanTimer.Start();
        serviceDiscovery.StartBrowse(ServiceType);
    }

    public void StopBrowse()
    {
        massStorageScanTimer.Stop();
        serviceDiscovery.StopBrowse();
        DataStores.Clear();
    }
}