using System.Collections.Generic;
using Sufni.App.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ServiceDiscovery;
using Timer = System.Timers.Timer;

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
    private static readonly object DataStoreLock = new();
    public ObservableCollection<ITelemetryDataStore> DataStores { get; } = new();

    private void GetMassStorageDatastores()
    {
        var x = DriveInfo.GetDrives();
        var comparer = new DriveInfoComparer();
        var current = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady &&
                (drive.DriveFormat == "FAT32" || drive.DriveFormat == "msdos") &&
                File.Exists($"{drive.RootDirectory}/BOARDID"))
            .ToArray();
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

        lock (DataStoreLock)
        {
            var toRemove = DataStores.First(x => x.Name == name);
            DataStores.Remove(toRemove);
        }
    }

    private async Task AddNetworkDataStore(ServiceAnnouncementEventArgs e)
    {
        var ipAddress = e.Announcement.Address;
        var port = e.Announcement.Port;

        var ds = new NetworkTelemetryDataStore(ipAddress, port);
        await ds.Initialization;

        lock (DataStoreLock)
        {
            DataStores.Add(ds);
        }
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

    public TelemetryDataStoreService()
    {
        var serviceDiscovery = App.Current?.Services?.GetKeyedService<IServiceDiscovery>("gosst");
        Debug.Assert(serviceDiscovery != null, nameof(serviceDiscovery) + " != null");

        serviceDiscovery.ServiceAdded += async (_, e) => await AddNetworkDataStore(e);
        serviceDiscovery.ServiceRemoved += (_, e) => RemoveNetworkDataStore(e);
        serviceDiscovery.StartBrowse(ServiceType);

        GetMassStorageDatastores();
        var timer = new Timer(1000);
        timer.AutoReset = true;
        timer.Elapsed += (_, _) =>
        {
            var removedStorageProviders = GetRemovedStorageProviders();
            lock (DataStoreLock)
            {
                foreach (var ds in removedStorageProviders)
                {
                    DataStores.Remove(ds);
                }
                GetMassStorageDatastores();
            }
        };
        timer.Enabled = true;
    }
}