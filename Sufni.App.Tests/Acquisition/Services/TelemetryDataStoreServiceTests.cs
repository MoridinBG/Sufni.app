using Avalonia.Platform.Storage;
using Microsoft.Extensions.Time.Testing;
using Sufni.App.Tests.TestSupport.Acquisition;
using System.Threading.Channels;
using System.Reactive.Linq;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using NSubstitute;
using static Sufni.App.Tests.TestSupport.Fixtures.TestStorageItems;

using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Async;
namespace Sufni.App.Tests.Acquisition.Services;

public class TelemetryDataStoreServiceTests
{
    [Fact]
    public async Task StartBrowse_AcquiresBrowseLeaseOnce_AndStopBrowseDisposesLeaseAndClearsStores()
    {
        var catalog = Substitute.For<ILiveDaqCatalogService>();
        catalog.Observe().Returns(Observable.Return<IReadOnlyList<LiveDaqCatalogEntry>>([]));
        var lease = Substitute.For<IDisposable>();
        catalog.AcquireBrowse().Returns(lease);
        await using var service = CreateService(catalog: catalog);
        service.DataStores.Add(CreateDataStore("DAQ"));

        service.StartBrowse();
        service.StartBrowse();
        service.StopBrowse();

        catalog.Received(1).AcquireBrowse();
        lease.Received(1).Dispose();
        Assert.Empty(service.DataStores);
    }

    [Fact]
    public async Task LoadFilesAsync_UsesBackgroundRunnerToReadDataStoreFiles()
    {
        var backgroundTaskRunner = new RecordingBackgroundTaskRunner();
        await using var service = CreateService(backgroundTaskRunner: backgroundTaskRunner);
        var file = Substitute.For<ITelemetryFile>();
        var dataStore = CreateDataStore("DAQ");
        dataStore.GetFiles(Arg.Any<CancellationToken>()).Returns(Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([file], null)));

        var files = await service.LoadFilesAsync(dataStore, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(file, Assert.Single(Assert.IsType<TelemetryFileListing.Listed>(files).Files));
        Assert.Equal(1, backgroundTaskRunner.InvocationCount);
        await dataStore.Received(1).GetFiles(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TryAddStorageProviderAsync_AddsInitializedStorageProvider()
    {
        await using var service = CreateService();
        var expectedBoardId = UuidUtil.CreateDeviceUuid("abcdef1234567890");
        var folder = CreateStorageProviderFolder("/tmp/sufni-daq", "ABCDEF1234567890");

        var result = await service.TryAddStorageProviderAsync(folder, cancellationToken: TestContext.Current.CancellationToken);

        var added = Assert.IsType<StorageProviderRegistrationResult.Added>(result);
        Assert.Same(added.DataStore, Assert.Single(service.DataStores));
        Assert.Equal("DAQ", added.DataStore.Name);
        Assert.Equal(expectedBoardId, added.DataStore.BoardId);
    }

    [Fact]
    public async Task TryAddStorageProviderAsync_ReturnsAlreadyOpen_WhenLocalPathMatchesExistingStorageProvider()
    {
        await using var service = CreateService();
        var existingFolder = CreateStorageProviderFolder("/tmp/sufni-daq", "ABCDEF1234567890");
        var existingStore = new StorageProviderTelemetryDataStore(existingFolder);
        await existingStore.Initialization;
        service.DataStores.Add(existingStore);

        var duplicateFolder = Substitute.For<IStorageFolder>();
        duplicateFolder.Name.Returns("DAQ duplicate");
        duplicateFolder.Path.Returns(new Uri("file:///tmp/sufni-daq"));

        var result = await service.TryAddStorageProviderAsync(duplicateFolder, cancellationToken: TestContext.Current.CancellationToken);

        var alreadyOpen = Assert.IsType<StorageProviderRegistrationResult.AlreadyOpen>(result);
        Assert.Same(existingStore, alreadyOpen.DataStore);
        Assert.Same(existingStore, Assert.Single(service.DataStores));
        duplicateFolder.DidNotReceive().GetItemsAsync();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CatalogPresence_CreatesNetworkSourcesWithoutOpeningDeviceConnections(bool supportsMassStorage, bool advertisedIdentity)
    {
        var network = new NetworkTelemetrySourceHarness();
        if (!advertisedIdentity) network.Entries.OnNext([network.Entries.Value.Single() with { BoardId = null }]);
        await using var service = CreateService(catalog: network.Catalog, registry: network.Registry,
            boardIdInspector: network.Inspector, management: network.Management,
            appEnvironment: CreateEnvironment(supportsMassStorage));

        service.StartBrowse();

        var store = Assert.Single(service.DataStores);
        Assert.Equal("gosst://127.0.0.1:5555", store.Name);
        Assert.Equal(advertisedIdentity ? network.BoardId : (Guid?)null, store.BoardId);
        await network.Inspector.DidNotReceive().InspectAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>());
        await network.Management.DidNotReceive().ListDirectoryAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CatalogChanges_ReplaceChangedEndpoints_IgnoreV3_AndRemoveOnlyNetworkSources()
    {
        var network = new NetworkTelemetrySourceHarness();
        var entry = network.Entries.Value.Single();
        network.Entries.OnNext([entry, entry with { IdentityKey = "v3", ProtocolVersion = LiveProtocolVersion.V3 }]);
        await using var service = CreateService(catalog: network.Catalog);
        var local = CreateDataStore("USB");
        service.DataStores.Add(local);
        service.StartBrowse();
        var first = Assert.Single(service.DataStores.OfType<NetworkTelemetryDataStore>());

        network.Entries.OnNext([entry]);
        Assert.Same(first, Assert.Single(service.DataStores.OfType<NetworkTelemetryDataStore>()));
        network.Entries.OnNext([entry with { NetworkEndpoint = new ServiceEndpoint("127.0.0.1", 6666) }]);
        var replacement = Assert.Single(service.DataStores.OfType<NetworkTelemetryDataStore>());
        Assert.NotSame(first, replacement);
        Assert.Equal("gosst://127.0.0.1:6666", replacement.Name);
        network.Entries.OnNext([]);

        Assert.Same(local, Assert.Single(service.DataStores));
    }

    [Fact]
    public async Task StopBrowse_RejectsQueuedCatalogPresence_AndRestartUsesCurrentCatalog()
    {
        var network = new NetworkTelemetrySourceHarness();
        var dispatcher = new ManualUiThreadDispatcher();
        await using var service = CreateService(catalog: network.Catalog, dispatcher: dispatcher);
        service.StartBrowse();
        service.StopBrowse();
        await dispatcher.DrainAsync();
        Assert.Empty(service.DataStores);

        service.StartBrowse();
        await dispatcher.DrainAsync();

        Assert.Single(service.DataStores);
    }

    [Fact]
    public async Task BrowseTicks_PublishDriveArrivalAndRemoval()
    {
        var time = new FakeTimeProvider();
        var drives = new FakeMassStorageDriveSource();
        var drive = new DaqDrive("/daq", "DAQ", "Trail DAQ");
        var boardId = Guid.NewGuid();
        drives.Drives.Add(drive);
        drives.BoardIds[drive.RootPath] = boardId;
        await using var service = CreateService(driveProbe: drives, timeProvider: time);
        var changes = Channel.CreateUnbounded<bool>();
        service.DataStores.CollectionChanged += (_, _) => changes.Writer.TryWrite(true);
        service.StartBrowse();

        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Empty(service.DataStores);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await changes.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        var store = Assert.Single(service.DataStores);
        Assert.Equal(boardId, store.BoardId);
        Assert.Equal("Trail DAQ", store.Name);

        drives.Drives.Clear();
        time.Advance(TimeSpan.FromSeconds(1));
        await changes.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(service.DataStores);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopBrowse_DiscardsDriveInitializationThatFinishesAfterStop(bool restart)
    {
        var time = new SignalingFakeTimeProvider();
        var probe = Substitute.For<IDaqDriveProbe>();
        var drive = new DaqDrive("/daq", "DAQ", "DAQ");
        probe.FindDrives().Returns([drive]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.InitializeAsync(drive, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            started.SetResult();
            return completion.Task;
        });
        await using var service = CreateService(driveProbe: probe, timeProvider: time);
        service.StartBrowse();
        await time.WaitForTimerAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        service.StopBrowse();
        if (restart) service.StartBrowse();
        completion.SetResult(Guid.NewGuid());
        if (restart)
            await time.WaitForTimerAsync(TestContext.Current.CancellationToken);
        else
            await service.DisposeAsync();

        Assert.Empty(service.DataStores);
    }

    [Fact]
    public async Task ScanFailure_IsReported_AndNextTickCanDiscoverDrive()
    {
        var time = new FakeTimeProvider();
        var probe = Substitute.For<IDaqDriveProbe>();
        var scanFails = true;
        var drive = new DaqDrive("/daq", "DAQ", "Recovered DAQ");
        probe.FindDrives().Returns(_ => scanFails ? throw new IOException("drive unavailable") : [drive]);
        probe.InitializeAsync(drive, Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        await using var service = CreateService(driveProbe: probe, timeProvider: time);
        var error = new TaskCompletionSource<DataStoreDiscoveryIssue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DiscoveryIssueReported += (_, message) => error.TrySetResult(message);
        service.DataStores.CollectionChanged += (_, _) => added.TrySetResult();
        service.StartBrowse();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(DataStoreDiscoveryFailureReason.Scan,
            (await error.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2))).Reason);

        scanFails = false;
        time.Advance(TimeSpan.FromSeconds(1));
        await added.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("Recovered DAQ", Assert.Single(service.DataStores).Name);
    }

    private static TelemetryDataStoreService CreateService(
        ILiveDaqCatalogService? catalog = null,
        ILiveDaqSharedStreamRegistry? registry = null,
        IBackgroundTaskRunner? backgroundTaskRunner = null,
        ILiveDaqBoardIdInspector? boardIdInspector = null,
        IDaqManagementService? management = null,
        IAppEnvironment? appEnvironment = null,
        IDaqDriveProbe? driveProbe = null,
        TimeProvider? timeProvider = null,
        IUiThreadDispatcher? dispatcher = null)
    {
        if (catalog is null)
        {
            catalog = Substitute.For<ILiveDaqCatalogService>();
            catalog.Observe().Returns(Observable.Return<IReadOnlyList<LiveDaqCatalogEntry>>([]));
            catalog.AcquireBrowse().Returns(Substitute.For<IDisposable>());
        }
        return new TelemetryDataStoreService(
            catalog,
            registry ?? Substitute.For<ILiveDaqSharedStreamRegistry>(),
            management ?? Substitute.For<IDaqManagementService>(),
            boardIdInspector ?? Substitute.For<ILiveDaqBoardIdInspector>(),
            backgroundTaskRunner ?? new InlineBackgroundTaskRunner(),
            driveProbe ?? new FakeMassStorageDriveSource(),
            dispatcher ?? new InlineUiThreadDispatcher(),
            appEnvironment ?? CreateEnvironment(supportsMassStorageImport: true),
            timeProvider ?? new FakeTimeProvider());
    }

    private static IAppEnvironment CreateEnvironment(bool supportsMassStorageImport) =>
        new AppEnvironment(
            LayoutProfile: UiLayoutProfile.Compact,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: false,
                CanPairAsClient: true,
                SupportsMassStorageImport: supportsMassStorageImport,
                SupportsStorageProviderImport: true,
                CanPlayVideo: false),
            Input: new InputCapabilities(
                HasPointer: false,
                HasTouch: true,
                HasKeyboard: false,
                SupportsLongPressContextMenu: true,
                CommandModifier: CommandModifier.Control));

    private static ITelemetryDataStore CreateDataStore(string name)
    {
        var dataStore = Substitute.For<ITelemetryDataStore>();
        dataStore.Name.Returns(name);
        return dataStore;
    }

    private static IStorageFolder CreateStorageProviderFolder(string localPath, string boardId)
    {
        var uploaded = Substitute.For<IStorageFolder>();
        uploaded.Name.Returns("uploaded");
        uploaded.Path.Returns(new Uri($"file://{localPath}/uploaded"));

        var boardIdFile = Substitute.For<IStorageFile>();
        boardIdFile.Name.Returns("BOARDID");
        boardIdFile.Path.Returns(new Uri($"file://{localPath}/BOARDID"));
        boardIdFile.OpenReadAsync().Returns(_ =>
            Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.ASCII.GetBytes(boardId))));

        var folder = Substitute.For<IStorageFolder>();
        folder.Name.Returns("DAQ");
        folder.Path.Returns(new Uri($"file://{localPath}"));
        folder.GetItemsAsync().Returns(_ => EnumerateStorageItems(boardIdFile, uploaded));
        return folder;
    }

}
