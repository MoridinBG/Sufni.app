using System.Text;
using Avalonia.Platform.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static Sufni.App.Tests.TestSupport.Fixtures.TestStorageItems;

using Sufni.App.Acquisition.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Acquisition.Models;

public class StorageProviderTelemetryDataStoreTests
{
    [Fact]
    public async Task Initialization_ReadsBoardId_AndCreatesUploadedFolderWhenMissing()
    {
        var folder = CreateFolder("DAQ");
        var uploaded = CreateFolder("uploaded");
        var boardIdFile = CreateBoardIdFile("ABCDEF1234567890");
        folder.GetItemsAsync().Returns(_ => EnumerateStorageItems(boardIdFile));
        folder.CreateFolderAsync("uploaded").Returns(Task.FromResult<IStorageFolder?>(uploaded));

        var dataStore = new StorageProviderTelemetryDataStore(folder);
        await dataStore.Initialization;

        Assert.Equal("DAQ", dataStore.Name);
        Assert.Equal(UuidUtil.CreateDeviceUuid("abcdef1234567890"), dataStore.BoardId);
        await folder.Received(1).CreateFolderAsync("uploaded");
    }

    [Fact]
    public async Task Initialization_DoesNotCreateUploadedFolder_WhenItAlreadyExists()
    {
        var folder = CreateFolder("DAQ");
        var uploaded = CreateFolder("uploaded");
        var boardIdFile = CreateBoardIdFile("0011223344556677");
        folder.GetItemsAsync().Returns(_ => EnumerateStorageItems(boardIdFile, uploaded));

        var dataStore = new StorageProviderTelemetryDataStore(folder);
        await dataStore.Initialization;

        Assert.Equal(UuidUtil.CreateDeviceUuid("0011223344556677"), dataStore.BoardId);
        await folder.DidNotReceive().CreateFolderAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task GetFiles_ReturnsValidSstFilesOrderedByStartTimeDescending()
    {
        var folder = CreateFolder("DAQ");
        var uploaded = CreateFolder("uploaded");
        var older = CreateStorageFile(
            "older.SST",
            TestSstFiles.CreateValidV3(timestamp: 100));
        var newer = CreateStorageFile(
            "newer.SST",
            TestSstFiles.CreateValidV3(timestamp: 200));
        var invalid = CreateStorageFile("broken.SST", [0x01, 0x02]);
        invalid.OpenReadAsync().ThrowsAsync(new InvalidDataException("not telemetry"));
        var ignored = CreateStorageFile("notes.txt", Encoding.UTF8.GetBytes("not telemetry"));

        folder.GetItemsAsync().Returns(_ => EnumerateStorageItems(uploaded, older, invalid, ignored, newer));

        var dataStore = new StorageProviderTelemetryDataStore(folder);
        var files = await dataStore.GetFiles();

        Assert.Collection(
            files,
            first => Assert.Equal("newer.SST", first.FileName),
            second => Assert.Equal("older.SST", second.FileName));
    }

    [Fact]
    public void IsAvailable_ReturnsFalse_WhenFolderEnumerationCannotStart()
    {
        var folder = CreateFolder("DAQ");
        folder.GetItemsAsync().Returns(_ => throw new InvalidOperationException("folder unavailable"));

        var dataStore = new StorageProviderTelemetryDataStore(folder);

        Assert.False(dataStore.IsAvailable());
    }

    private static IStorageFolder CreateFolder(string name)
    {
        var folder = Substitute.For<IStorageFolder>();
        folder.Name.Returns(name);
        folder.Path.Returns(new Uri($"file:///tmp/{Guid.NewGuid():N}/{name}"));
        return folder;
    }

    private static IStorageFile CreateBoardIdFile(string serialHex)
    {
        var file = CreateStorageFile("BOARDID", Encoding.ASCII.GetBytes(serialHex));
        return file;
    }

    private static IStorageFile CreateStorageFile(string name, byte[] bytes)
    {
        var file = Substitute.For<IStorageFile>();
        file.Name.Returns(name);
        file.Path.Returns(new Uri($"file:///tmp/{Guid.NewGuid():N}/{name}"));
        file.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream(bytes)));
        return file;
    }
}
