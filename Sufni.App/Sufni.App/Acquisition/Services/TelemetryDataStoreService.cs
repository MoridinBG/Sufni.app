using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using System.Reactive.Linq;
using Serilog;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
namespace Sufni.App.Acquisition.Services;

internal sealed class TelemetryDataStoreService : ITelemetryDataStoreService, IAsyncDisposable
{
    private static readonly ILogger logger = Log.ForContext<TelemetryDataStoreService>();
    private readonly IDaqDriveProbe driveProbe;
    private readonly ILiveDaqCatalogService catalog;
    private readonly ILiveDaqSharedStreamRegistry registry;
    private IDisposable? catalogSubscription;
    private long browseGeneration;
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
    public event EventHandler<DataStoreDiscoveryIssue>? DiscoveryIssueReported;

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

    private void ApplyCatalog(IReadOnlyList<LiveDaqCatalogEntry> entries, long generation)
    {
        if (!isBrowsing || generation != browseGeneration) return;
        var available = entries.Where(entry => entry.SupportsManagement).ToDictionary(entry => entry.IdentityKey, StringComparer.OrdinalIgnoreCase);
        foreach (var existing in DataStores.OfType<NetworkTelemetryDataStore>().ToArray())
        {
            if (!available.Remove(existing.CatalogEntry.IdentityKey, out var entry)) DataStores.Remove(existing);
            else if (existing.CatalogEntry != entry)
                DataStores[DataStores.IndexOf(existing)] = new NetworkTelemetryDataStore(entry, catalog, registry, daqManagementService, liveDaqBoardIdInspector);
        }
        foreach (var entry in available.Values)
            DataStores.Add(new NetworkTelemetryDataStore(entry, catalog, registry, daqManagementService, liveDaqBoardIdInspector));
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
        ILiveDaqCatalogService catalog,
        ILiveDaqSharedStreamRegistry registry,
        IDaqManagementService daqManagementService,
        ILiveDaqBoardIdInspector liveDaqBoardIdInspector,
        IBackgroundTaskRunner backgroundTaskRunner,
        IDaqDriveProbe driveProbe,
        IUiThreadDispatcher uiThreadDispatcher,
        IAppEnvironment appEnvironment,
        TimeProvider timeProvider)
    {
        this.driveProbe = driveProbe;
        this.catalog = catalog;
        this.registry = registry;
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
                        logger.Error(exception, "DAQ drive scan failed");
                        await uiThreadDispatcher.InvokeAsync(() =>
                        {
                            if (!token.IsCancellationRequested)
                                DiscoveryIssueReported?.Invoke(this, new DataStoreDiscoveryIssue(DataStoreDiscoveryFailureReason.Scan, exception.Message));
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
        var generation = ++browseGeneration;
        catalogSubscription = catalog.Observe().ObserveOnUiThread(uiThreadDispatcher)
            .Subscribe(entries => ApplyCatalog(entries, generation));
        if (supportsMassStorageImport)
        {
            massStorageScanCancellation = new CancellationTokenSource();
            massStorageScanTask = RunMassStorageScanLoopAsync(massStorageScanTask, massStorageScanCancellation);
        }

        browseLease = catalog.AcquireBrowse();
    }

    public void StopBrowse()
    {
        if (!isBrowsing)
            return;

        isBrowsing = false;
        catalogSubscription?.Dispose();
        catalogSubscription = null;
        massStorageScanCancellation?.Cancel();
        massStorageScanCancellation = null;
        browseLease?.Dispose();
        browseLease = null;
        DataStores.Clear();
    }

    public Task<TelemetryFileListing> LoadFilesAsync(
        ITelemetryDataStore dataStore,
        CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync(() => dataStore.GetFiles(cancellationToken), cancellationToken);

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
