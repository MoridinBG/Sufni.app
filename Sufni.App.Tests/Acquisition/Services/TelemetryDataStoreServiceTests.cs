using Avalonia.Platform.Storage;
using Microsoft.Extensions.Time.Testing;
using Sufni.App.Tests.TestSupport.Acquisition;
using System.Threading.Channels;
using System.Net;
using NSubstitute;
using static Sufni.App.Tests.TestSupport.Fixtures.TestStorageItems;

using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services;
using Sufni.App.Shared.Common;
using Sufni.App.Tests.TestSupport.Async;
namespace Sufni.App.Tests.Acquisition.Services;

public class TelemetryDataStoreServiceTests
{
    [Fact]
    public async Task StartBrowse_AcquiresBrowseLeaseOnce_AndStopBrowseDisposesLeaseAndClearsStores()
    {
        var browseOwner = Substitute.For<IDaqBrowseOwner>();
        var lease = Substitute.For<IDisposable>();
        browseOwner.AcquireBrowse().Returns(lease);
        await using var service = CreateService(browseOwner: browseOwner);
        service.DataStores.Add(CreateDataStore("DAQ"));

        service.StartBrowse();
        service.StartBrowse();
        service.StopBrowse();

        browseOwner.Received(1).AcquireBrowse();
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
        dataStore.GetFiles().Returns(Task.FromResult(new List<ITelemetryFile> { file }));

        var files = await service.LoadFilesAsync(dataStore, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(file, Assert.Single(files));
        Assert.Equal(1, backgroundTaskRunner.InvocationCount);
        await dataStore.Received(1).GetFiles();
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

    [Fact]
    public async Task ServiceAdded_AddsDiscoveredNetworkDataStore()
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        var boardIdInspector = Substitute.For<ILiveDaqBoardIdInspector>();
        var boardId = Guid.NewGuid();
        boardIdInspector.InspectAsync(IPAddress.Loopback, 5555, cancellationToken: Arg.Any<CancellationToken>()).Returns(Task.FromResult<Guid?>(boardId));
        await using var service = CreateService(serviceDiscovery: serviceDiscovery, boardIdInspector: boardIdInspector);
        service.StartBrowse();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(CreateDaqAnnouncement(IPAddress.Loopback, 5555, "2")));

        var store = Assert.Single(service.DataStores);
        Assert.Equal("gosst://127.0.0.1:5555", store.Name);
        Assert.Equal(boardId, store.BoardId);
    }

    [Fact]
    public async Task ServiceAdded_AddsDiscoveredNetworkDataStore_WhenMassStorageUnsupported()
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        var boardIdInspector = Substitute.For<ILiveDaqBoardIdInspector>();
        var boardId = Guid.NewGuid();
        boardIdInspector.InspectAsync(IPAddress.Loopback, 5555, cancellationToken: Arg.Any<CancellationToken>()).Returns(Task.FromResult<Guid?>(boardId));
        await using var service = CreateService(
            serviceDiscovery: serviceDiscovery,
            boardIdInspector: boardIdInspector,
            appEnvironment: CreateEnvironment(supportsMassStorageImport: false));
        service.StartBrowse();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(CreateDaqAnnouncement(IPAddress.Loopback, 5555, "2")));

        var store = Assert.Single(service.DataStores);
        Assert.Equal("gosst://127.0.0.1:5555", store.Name);
        Assert.Equal(boardId, store.BoardId);
    }

    [Fact]
    public async Task ServiceAdded_DoesNotDuplicateExistingStore()
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        await using var service = CreateService(serviceDiscovery: serviceDiscovery);
        service.StartBrowse();
        var announcement = new ServiceAnnouncementEventArgs(CreateDaqAnnouncement(IPAddress.Loopback, 5555, "2"));

        serviceDiscovery.ServiceAdded += Raise.EventWith(announcement);
        serviceDiscovery.ServiceAdded += Raise.EventWith(announcement);

        Assert.Single(service.DataStores);
    }

    [Fact]
    public async Task ServiceAdded_ReportsError_WhenDiscoveryInitializationFails()
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        var boardIdInspector = Substitute.For<ILiveDaqBoardIdInspector>();
        boardIdInspector.InspectAsync(
                Arg.Any<IPAddress>(),
                Arg.Any<int>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns<Task<Guid?>>(_ => throw new IOException("unreachable"));
        await using var service = CreateService(serviceDiscovery: serviceDiscovery, boardIdInspector: boardIdInspector);
        var errors = new List<string>();
        service.ErrorOccurred += (_, message) => errors.Add(message);
        service.StartBrowse();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(CreateDaqAnnouncement(IPAddress.Loopback, 5555, "2")));

        Assert.Empty(service.DataStores);
        Assert.Single(errors);
    }

    [Fact]
    public async Task ServiceRemoved_RemovesTheMatchingStoreOnly()
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        await using var service = CreateService(serviceDiscovery: serviceDiscovery);
        service.StartBrowse();
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(CreateDaqAnnouncement(IPAddress.Loopback, 5555, "2")));
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(CreateDaqAnnouncement(IPAddress.Loopback, 6666, "2")));

        serviceDiscovery.ServiceRemoved += Raise.EventWith(
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Loopback, 5555)));

        var remaining = Assert.Single(service.DataStores);
        Assert.Equal("gosst://127.0.0.1:6666", remaining.Name);
    }

    [Fact]
    public async Task ServiceAnnouncements_AreIgnoredAfterStopBrowse()
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        await using var service = CreateService(serviceDiscovery: serviceDiscovery);
        service.StartBrowse();
        service.StopBrowse();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(CreateDaqAnnouncement(IPAddress.Loopback, 5555, "2")));

        Assert.Empty(service.DataStores);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("3")]
    [InlineData("4")]
    public async Task ServiceAdded_IgnoresAnnouncementsWithoutV2Protocol(string? liveProto)
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        var boardIdInspector = Substitute.For<ILiveDaqBoardIdInspector>();
        await using var service = CreateService(serviceDiscovery: serviceDiscovery, boardIdInspector: boardIdInspector);
        service.StartBrowse();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(CreateDaqAnnouncement(IPAddress.Loopback, 5555, liveProto)));

        Assert.Empty(service.DataStores);
        await boardIdInspector.DidNotReceive().InspectAsync(
            Arg.Any<IPAddress>(),
            Arg.Any<int>(),
            cancellationToken: Arg.Any<CancellationToken>());
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
        var error = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ErrorOccurred += (_, message) => error.TrySetResult(message);
        service.DataStores.CollectionChanged += (_, _) => added.TrySetResult();
        service.StartBrowse();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Contains("drive unavailable", await error.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2)));

        scanFails = false;
        time.Advance(TimeSpan.FromSeconds(1));
        await added.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("Recovered DAQ", Assert.Single(service.DataStores).Name);
    }

    private static TelemetryDataStoreService CreateService(
        IDaqBrowseOwner? browseOwner = null,
        IBackgroundTaskRunner? backgroundTaskRunner = null,
        IServiceDiscovery? serviceDiscovery = null,
        ILiveDaqBoardIdInspector? boardIdInspector = null,
        IAppEnvironment? appEnvironment = null,
        IDaqDriveProbe? driveProbe = null,
        TimeProvider? timeProvider = null)
    {
        if (browseOwner is null)
        {
            browseOwner = Substitute.For<IDaqBrowseOwner>();
            browseOwner.AcquireBrowse().Returns(Substitute.For<IDisposable>());
        }

        return new TelemetryDataStoreService(
            serviceDiscovery ?? Substitute.For<IServiceDiscovery>(),
            browseOwner,
            Substitute.For<IDaqManagementService>(),
            boardIdInspector ?? Substitute.For<ILiveDaqBoardIdInspector>(),
            backgroundTaskRunner ?? new InlineBackgroundTaskRunner(),
            driveProbe ?? new FakeMassStorageDriveSource(),
            new InlineUiThreadDispatcher(),
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

    private static ServiceAnnouncement CreateDaqAnnouncement(IPAddress address, ushort port, string? liveProto)
    {
        var txtRecords = new Dictionary<string, string>(StringComparer.Ordinal);
        if (liveProto is not null)
        {
            txtRecords.Add("live_proto", liveProto);
        }

        return new ServiceAnnouncement(address, port, instanceName: null, txtRecords);
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
