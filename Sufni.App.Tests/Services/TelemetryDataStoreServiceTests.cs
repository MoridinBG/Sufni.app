using Avalonia.Platform.Storage;
using NSubstitute;
using Sufni.App.ExtensionHost.Services;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Services;

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
        Assert.Equal(1, backgroundTaskRunner.AsyncValueRunCount);
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

    private static TelemetryDataStoreService CreateService(
        IDaqBrowseOwner? browseOwner = null,
        IBackgroundTaskRunner? backgroundTaskRunner = null)
    {
        if (browseOwner is null)
        {
            browseOwner = Substitute.For<IDaqBrowseOwner>();
            browseOwner.AcquireBrowse().Returns(Substitute.For<IDisposable>());
        }

        return new TelemetryDataStoreService(
            Substitute.For<IServiceDiscovery>(),
            browseOwner,
            Substitute.For<IDaqManagementService>(),
            Substitute.For<ILiveDaqBoardIdInspector>(),
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

    private static async IAsyncEnumerable<IStorageItem> EnumerateStorageItems(params IStorageItem[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private sealed class RecordingBackgroundTaskRunner : IBackgroundTaskRunner
    {
        public int AsyncValueRunCount { get; private set; }

        public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();

        public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default) =>
            Task.FromResult(work());

        public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
        {
            AsyncValueRunCount++;
            return await work();
        }
    }
}
