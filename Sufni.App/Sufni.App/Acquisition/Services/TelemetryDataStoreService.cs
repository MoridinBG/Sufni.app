using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services;
using Sufni.App.Shared.Common;
namespace Sufni.App.Acquisition.Services;

internal sealed class TelemetryDataStoreService : ITelemetryDataStoreService, IAsyncDisposable
{
    private static readonly ILogger logger = Log.ForContext<TelemetryDataStoreService>();
    private readonly IDaqDriveProbe driveProbe;
    private readonly IServiceDiscovery serviceDiscovery;
    private readonly IDaqBrowseOwner browseOwner;
    private readonly IDaqManagementService daqManagementService;
    private readonly ILiveDaqBoardIdInspector liveDaqBoardIdInspector;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly TimeProvider timeProvider;
    private CancellationTokenSource? massStorageScanCancellation;
    private Task massStorageScanTask = Task.CompletedTask;
    private readonly bool supportsMassStorageImport;
    private volatile bool isBrowsing;
    private IDisposable? browseLease;

    public ObservableCollection<ITelemetryDataStore> DataStores { get; } = new();
    public event EventHandler<string>? ErrorOccurred;

    private async Task RefreshMassStorageDataStoresAsync(CancellationToken cancellationToken = default)
    {
        if (!supportsMassStorageImport)
            return;

        var (knownMassStorageDrives, storageProviderDataStores) = await uiThreadDispatcher.InvokeAsync(() => (
            DataStores.OfType<MassStorageTelemetryDataStore>().Select(store => store.Drive).ToArray(),
            DataStores.OfType<StorageProviderTelemetryDataStore>().ToArray()));

        var currentMassStorageDrives = await GetCurrentMassStorageDrivesAsync(cancellationToken);
        var removedStorageProviders = await backgroundTaskRunner.RunAsync(
            () => GetRemovedStorageProviders(storageProviderDataStores),
            cancellationToken);

        var addedDrives = currentMassStorageDrives.ExceptBy(knownMassStorageDrives.Select(drive => drive.VolumeLabel), drive => drive.VolumeLabel).ToArray();
        var removedDrives = knownMassStorageDrives.ExceptBy(currentMassStorageDrives.Select(drive => drive.VolumeLabel), drive => drive.VolumeLabel).ToArray();
        var addedDataStores = new List<MassStorageTelemetryDataStore>(addedDrives.Length);

        foreach (var drive in addedDrives)
        {
            addedDataStores.Add(await CreateMassStorageDataStoreAsync(drive, cancellationToken));
        }

        await uiThreadDispatcher.InvokeAsync(() =>
        {
            if (!isBrowsing || cancellationToken.IsCancellationRequested)
                return;

            foreach (var dataStore in removedStorageProviders)
            {
                DataStores.Remove(dataStore);
            }

            foreach (var drive in removedDrives)
            {
                var toRemove = DataStores.FirstOrDefault(ds =>
                    ds is MassStorageTelemetryDataStore massStorageDataStore &&
                    massStorageDataStore.Drive.VolumeLabel == drive.VolumeLabel);
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

    private async Task RemoveNetworkDataStoreAsync(ServiceAnnouncementEventArgs e)
    {
        var ipAddress = e.Announcement.Address;
        var port = e.Announcement.Port;
        var name = $"gosst://{ipAddress}:{port}";

        await uiThreadDispatcher.InvokeAsync(() =>
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

        NetworkTelemetryDataStore ds;
        try
        {
            ds = new NetworkTelemetryDataStore(ipAddress, port, daqManagementService, liveDaqBoardIdInspector);
            await ds.Initialization;
        }
        catch (Exception ex)
        {
            await uiThreadDispatcher.InvokeAsync(() =>
                ErrorOccurred?.Invoke(this, $"Could not connect to DAQ at {ipAddress}:{port}: {ex.Message}"));
            return;
        }

        await uiThreadDispatcher.InvokeAsync(() =>
        {
            if (!isBrowsing || DataStores.Any(existing => existing.Name == ds.Name))
                return;

            DataStores.Add(ds);
        });
    }

    private Task<IReadOnlyList<DaqDrive>> GetCurrentMassStorageDrivesAsync(CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync(driveProbe.FindDrives, cancellationToken);

    private Task<MassStorageTelemetryDataStore> CreateMassStorageDataStoreAsync(
        DaqDrive drive,
        CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync(
            () => MassStorageTelemetryDataStore.CreateAsync(drive, driveProbe, cancellationToken),
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
        [FromKeyedServices("daq")] IServiceDiscovery serviceDiscovery,
        IDaqBrowseOwner browseOwner,
        IDaqManagementService daqManagementService,
        ILiveDaqBoardIdInspector liveDaqBoardIdInspector,
        IBackgroundTaskRunner backgroundTaskRunner,
        IDaqDriveProbe driveProbe,
        IUiThreadDispatcher uiThreadDispatcher,
        IAppEnvironment appEnvironment,
        TimeProvider timeProvider)
    {
        this.driveProbe = driveProbe;
        this.serviceDiscovery = serviceDiscovery;
        this.browseOwner = browseOwner;
        this.daqManagementService = daqManagementService;
        this.liveDaqBoardIdInspector = liveDaqBoardIdInspector;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.uiThreadDispatcher = uiThreadDispatcher;
        supportsMassStorageImport = appEnvironment.Capabilities.SupportsMassStorageImport;

        this.timeProvider = timeProvider;
    }

    private async Task RunMassStorageScanLoopAsync(Task previousScan, CancellationTokenSource cancellation)
    {
        using (cancellation)
        {
            var token = cancellation.Token;
            try
            {
                await previousScan;
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), timeProvider);
                while (await timer.WaitForNextTickAsync(token))
                {
                    try
                    {
                        await RefreshMassStorageDataStoresAsync(token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        await uiThreadDispatcher.InvokeAsync(() =>
                        {
                            if (!token.IsCancellationRequested)
                                ErrorOccurred?.Invoke(this, $"Could not scan DAQ drives: {exception.Message}");
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopBrowse();
        await massStorageScanTask;
    }

    public void StartBrowse()
    {
        if (isBrowsing)
            return;

        isBrowsing = true;
        serviceDiscovery.ServiceAdded += OnServiceAdded;
        serviceDiscovery.ServiceRemoved += OnServiceRemoved;
        if (supportsMassStorageImport)
        {
            massStorageScanCancellation = new CancellationTokenSource();
            massStorageScanTask = RunMassStorageScanLoopAsync(massStorageScanTask, massStorageScanCancellation);
        }

        browseLease = browseOwner.AcquireBrowse();
    }

    public void StopBrowse()
    {
        if (!isBrowsing)
            return;

        isBrowsing = false;
        serviceDiscovery.ServiceAdded -= OnServiceAdded;
        serviceDiscovery.ServiceRemoved -= OnServiceRemoved;
        massStorageScanCancellation?.Cancel();
        massStorageScanCancellation = null;
        browseLease?.Dispose();
        browseLease = null;
        DataStores.Clear();
    }

    private async void OnServiceAdded(object? sender, ServiceAnnouncementEventArgs e)
    {
        if (!IsV2DaqAnnouncement(e.Announcement))
        {
            logger.Verbose(
                "Ignoring network import announcement from {Address}:{Port} because TXT live_proto is not 2",
                e.Announcement.Address,
                e.Announcement.Port);
            return;
        }

        await AddNetworkDataStoreAsync(e);
    }

    private async void OnServiceRemoved(object? sender, ServiceAnnouncementEventArgs e) =>
        await RemoveNetworkDataStoreAsync(e);

    private static bool IsV2DaqAnnouncement(ServiceAnnouncement announcement) =>
        announcement.TxtRecords.Any(record =>
            string.Equals(record.Key, "live_proto", StringComparison.Ordinal) &&
            string.Equals(record.Value, "2", StringComparison.Ordinal));

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

        await uiThreadDispatcher.InvokeAsync(() => DataStores.Add(dataStore));
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
                    massStorageDataStore.Drive.RootPath,
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
}
