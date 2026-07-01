using Avalonia.Platform.Storage;
using System.Net;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Services;
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
    public void StartBrowse_AcquiresBrowseLeaseOnce_AndStopBrowseDisposesLeaseAndClearsStores()
    {
        var browseOwner = Substitute.For<IDaqBrowseOwner>();
        var lease = Substitute.For<IDisposable>();
        browseOwner.AcquireBrowse().Returns(lease);
        var service = CreateService(browseOwner: browseOwner);
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
        var service = CreateService(backgroundTaskRunner: backgroundTaskRunner);
        var file = Substitute.For<ITelemetryFile>();
        var dataStore = CreateDataStore("DAQ");
        dataStore.GetFiles().Returns(Task.FromResult(new List<ITelemetryFile> { file }));

        var files = await service.LoadFilesAsync(dataStore);

        Assert.Same(file, Assert.Single(files));
        Assert.Equal(1, backgroundTaskRunner.InvocationCount);
        await dataStore.Received(1).GetFiles();
    }

    [Fact]
    public async Task TryAddStorageProviderAsync_AddsInitializedStorageProvider()
    {
        var service = CreateService();
        var expectedBoardId = UuidUtil.CreateDeviceUuid("abcdef1234567890");
        var folder = CreateStorageProviderFolder("/tmp/sufni-daq", "ABCDEF1234567890");

        var result = await service.TryAddStorageProviderAsync(folder);

        var added = Assert.IsType<StorageProviderRegistrationResult.Added>(result);
        Assert.Same(added.DataStore, Assert.Single(service.DataStores));
        Assert.Equal("DAQ", added.DataStore.Name);
        Assert.Equal(expectedBoardId, added.DataStore.BoardId);
    }

    [Fact]
    public async Task TryAddStorageProviderAsync_ReturnsAlreadyOpen_WhenLocalPathMatchesExistingStorageProvider()
    {
        var service = CreateService();
        var existingFolder = CreateStorageProviderFolder("/tmp/sufni-daq", "ABCDEF1234567890");
        var existingStore = new StorageProviderTelemetryDataStore(existingFolder);
        await existingStore.Initialization;
        service.DataStores.Add(existingStore);

        var duplicateFolder = Substitute.For<IStorageFolder>();
        duplicateFolder.Name.Returns("DAQ duplicate");
        duplicateFolder.Path.Returns(new Uri("file:///tmp/sufni-daq"));

        var result = await service.TryAddStorageProviderAsync(duplicateFolder);

        var alreadyOpen = Assert.IsType<StorageProviderRegistrationResult.AlreadyOpen>(result);
        Assert.Same(existingStore, alreadyOpen.DataStore);
        Assert.Same(existingStore, Assert.Single(service.DataStores));
        duplicateFolder.DidNotReceive().GetItemsAsync();
    }

    [Fact]
    public void ServiceAdded_AddsDiscoveredNetworkDataStore()
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        var boardIdInspector = Substitute.For<ILiveDaqBoardIdInspector>();
        var boardId = Guid.NewGuid();
        boardIdInspector.InspectAsync(IPAddress.Loopback, 5555).Returns(Task.FromResult<Guid?>(boardId));
        var service = CreateService(serviceDiscovery: serviceDiscovery, boardIdInspector: boardIdInspector);
        service.StartBrowse();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Loopback, 5555)));

        var store = Assert.Single(service.DataStores);
        Assert.Equal("gosst://127.0.0.1:5555", store.Name);
        Assert.Equal(boardId, store.BoardId);
    }

    [Fact]
    public void ServiceAdded_DoesNotDuplicateExistingStore()
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        var service = CreateService(serviceDiscovery: serviceDiscovery);
        service.StartBrowse();
        var announcement = new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Loopback, 5555));

        serviceDiscovery.ServiceAdded += Raise.EventWith(announcement);
        serviceDiscovery.ServiceAdded += Raise.EventWith(announcement);

        Assert.Single(service.DataStores);
    }

    [Fact]
    public void ServiceAdded_ReportsError_WhenDiscoveryInitializationFails()
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        var boardIdInspector = Substitute.For<ILiveDaqBoardIdInspector>();
        boardIdInspector.InspectAsync(Arg.Any<IPAddress>(), Arg.Any<int>())
            .Returns<Task<Guid?>>(_ => throw new IOException("unreachable"));
        var service = CreateService(serviceDiscovery: serviceDiscovery, boardIdInspector: boardIdInspector);
        var errors = new List<string>();
        service.ErrorOccurred += (_, message) => errors.Add(message);
        service.StartBrowse();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Loopback, 5555)));

        Assert.Empty(service.DataStores);
        Assert.Single(errors);
    }

    [Fact]
    public void ServiceRemoved_RemovesTheMatchingStoreOnly()
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        var service = CreateService(serviceDiscovery: serviceDiscovery);
        service.StartBrowse();
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Loopback, 5555)));
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Loopback, 6666)));

        serviceDiscovery.ServiceRemoved += Raise.EventWith(
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Loopback, 5555)));

        var remaining = Assert.Single(service.DataStores);
        Assert.Equal("gosst://127.0.0.1:6666", remaining.Name);
    }

    [Fact]
    public void ServiceAnnouncements_AreIgnoredAfterStopBrowse()
    {
        var serviceDiscovery = Substitute.For<IServiceDiscovery>();
        var service = CreateService(serviceDiscovery: serviceDiscovery);
        service.StartBrowse();
        service.StopBrowse();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Loopback, 5555)));

        Assert.Empty(service.DataStores);
    }

    private static TelemetryDataStoreService CreateService(
        IDaqBrowseOwner? browseOwner = null,
        IBackgroundTaskRunner? backgroundTaskRunner = null,
        IServiceDiscovery? serviceDiscovery = null,
        ILiveDaqBoardIdInspector? boardIdInspector = null)
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
            new InlineUiThreadDispatcher());
    }

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
