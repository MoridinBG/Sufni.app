using System;
using System.Collections.Generic;
using Sufni.App.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

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

internal sealed class TelemetryDataStoreService : ITelemetryDataStoreService
{
    private const string ServiceType = "_gosst._tcp";
    private readonly IServiceDiscovery serviceDiscovery;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly DispatcherTimer massStorageScanTimer;
    private int massStorageRefreshInProgress;
    private volatile bool isBrowsing;

    public ObservableCollection<ITelemetryDataStore> DataStores { get; } = new();

    private async Task RefreshMassStorageDataStoresAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref massStorageRefreshInProgress, 1) == 1)
            return;

        var comparer = new DriveInfoComparer();
        try
        {
            var knownMassStorageDrives = DataStores
                .OfType<MassStorageTelemetryDataStore>()
                .Select(ds => ds.DriveInfo)
                .ToArray();
            var storageProviderDataStores = DataStores
                .OfType<StorageProviderTelemetryDataStore>()
                .ToArray();

            var currentMassStorageDrives = await GetCurrentMassStorageDrivesAsync(cancellationToken);
            var removedStorageProviders = await backgroundTaskRunner.RunAsync(
                () => GetRemovedStorageProviders(storageProviderDataStores),
                cancellationToken);

            var addedDrives = currentMassStorageDrives.Except(knownMassStorageDrives, comparer).ToArray();
            var removedDrives = knownMassStorageDrives.Except(currentMassStorageDrives, comparer).ToArray();
            var addedDataStores = new List<MassStorageTelemetryDataStore>(addedDrives.Length);

            foreach (var drive in addedDrives)
            {
                addedDataStores.Add(await CreateMassStorageDataStoreAsync(drive, cancellationToken));
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!isBrowsing)
                    return;

                foreach (var dataStore in removedStorageProviders)
                {
                    DataStores.Remove(dataStore);
                }

                foreach (var drive in removedDrives)
                {
                    var toRemove = DataStores.FirstOrDefault(ds =>
                        ds is MassStorageTelemetryDataStore massStorageDataStore &&
                        comparer.Equals(massStorageDataStore.DriveInfo, drive));
                    if (toRemove is not null)
                    {
                        DataStores.Remove(toRemove);
                    }
                }

                foreach (var dataStore in addedDataStores)
                {
                    DataStores.Add(dataStore);
                }
            });
        }
        finally
        {
            Interlocked.Exchange(ref massStorageRefreshInProgress, 0);
        }
    }

    private async Task RemoveNetworkDataStoreAsync(ServiceAnnouncementEventArgs e)
    {
        var ipAddress = e.Announcement.Address;
        var port = e.Announcement.Port;
        var name = $"gosst://{ipAddress}:{port}";

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!isBrowsing)
                return;

            var toRemove = DataStores.FirstOrDefault(x => x.Name == name);
            if (toRemove is not null)
            {
                DataStores.Remove(toRemove);
            }
        });
    }

    private async Task AddNetworkDataStoreAsync(ServiceAnnouncementEventArgs e)
    {
        var ipAddress = e.Announcement.Address;
        var port = e.Announcement.Port;

        var ds = new NetworkTelemetryDataStore(ipAddress, port);
        await ds.Initialization;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!isBrowsing || DataStores.Any(existing => existing.Name == ds.Name))
                return;

            DataStores.Add(ds);
        });
    }

    private Task<IReadOnlyList<DriveInfo>> GetCurrentMassStorageDrivesAsync(CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync<IReadOnlyList<DriveInfo>>(
            () => DriveInfo.GetDrives()
                .Where(drive => drive is { IsReady: true } &&
                                File.Exists(Path.Combine(drive.RootDirectory.FullName, "BOARDID")))
                .ToArray(),
            cancellationToken);

    private Task<MassStorageTelemetryDataStore> CreateMassStorageDataStoreAsync(
        DriveInfo drive,
        CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync(
            () => MassStorageTelemetryDataStore.CreateAsync(drive, cancellationToken),
            cancellationToken);

    private static IReadOnlyList<StorageProviderTelemetryDataStore> GetRemovedStorageProviders(
        IReadOnlyList<StorageProviderTelemetryDataStore> storageProviderDatastores)
    {
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

    public TelemetryDataStoreService(
        [FromKeyedServices("gosst")] IServiceDiscovery serviceDiscovery,
        IBackgroundTaskRunner backgroundTaskRunner)
    {
        this.serviceDiscovery = serviceDiscovery;
        this.backgroundTaskRunner = backgroundTaskRunner;

        serviceDiscovery.ServiceAdded += async (_, e) => await AddNetworkDataStoreAsync(e);
        serviceDiscovery.ServiceRemoved += async (_, e) => await RemoveNetworkDataStoreAsync(e);

        massStorageScanTimer = new DispatcherTimer(DispatcherPriority.Background);
        massStorageScanTimer.Interval = TimeSpan.FromSeconds(1);
        massStorageScanTimer.Tick += async (_, _) => await RefreshMassStorageDataStoresAsync();
    }

    public void StartBrowse()
    {
        isBrowsing = true;
        massStorageScanTimer.Start();
        serviceDiscovery.StartBrowse(ServiceType);
    }

    public void StopBrowse()
    {
        isBrowsing = false;
        massStorageScanTimer.Stop();
        serviceDiscovery.StopBrowse();
        DataStores.Clear();
    }

    public Task<IReadOnlyList<ITelemetryFile>> LoadFilesAsync(
        ITelemetryDataStore dataStore,
        CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync(async () =>
        {
            var files = await dataStore.GetFiles();
            return (IReadOnlyList<ITelemetryFile>)files;
        }, cancellationToken);

    public async Task<StorageProviderRegistrationResult> TryAddStorageProviderAsync(
        IStorageFolder folder,
        CancellationToken cancellationToken = default)
    {
        var folderLocalPath = folder.TryGetLocalPath();
        var existing = DataStores.FirstOrDefault(ds => MatchesFolder(ds, folderLocalPath));
        if (existing is not null)
        {
            return new StorageProviderRegistrationResult.AlreadyOpen(existing);
        }

        var dataStore = await backgroundTaskRunner.RunAsync(async () =>
        {
            var createdDataStore = new StorageProviderTelemetryDataStore(folder);
            await createdDataStore.Initialization;
            return createdDataStore;
        }, cancellationToken);

        await Dispatcher.UIThread.InvokeAsync(() => DataStores.Add(dataStore));
        return new StorageProviderRegistrationResult.Added(dataStore);
    }

    private static bool MatchesFolder(ITelemetryDataStore dataStore, string? folderLocalPath)
    {
        if (string.IsNullOrEmpty(folderLocalPath))
            return false;

        return dataStore switch
        {
            MassStorageTelemetryDataStore massStorageDataStore =>
                string.Equals(
                    massStorageDataStore.DriveInfo.RootDirectory.FullName,
                    folderLocalPath,
                    StringComparison.OrdinalIgnoreCase),
            StorageProviderTelemetryDataStore storageProviderDataStore =>
                string.Equals(
                    storageProviderDataStore.LocalPath,
                    folderLocalPath,
                    StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public Task<Guid?> DetectConnectedBoardIdAsync(CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync(() =>
        {
            // Pure read: do not construct a MassStorageTelemetryDataStore,
            // its constructor creates the uploaded/ subdirectory as a side
            // effect. Mass-storage only — the network case requires async
            // mDNS discovery and is not the realistic first-run scenario.
            try
            {
                var drive = DriveInfo.GetDrives()
                    .FirstOrDefault(d => d is { IsReady: true } &&
                                         File.Exists(Path.Combine(d.RootDirectory.FullName, "BOARDID")));
                if (drive is null) return (Guid?)null;

                var serialHex = File.ReadAllText(
                    Path.Combine(drive.RootDirectory.FullName, "BOARDID")).ToLower();
                return UuidUtil.CreateDeviceUuid(serialHex);
            }
            catch
            {
                return null;
            }
        }, cancellationToken);
}