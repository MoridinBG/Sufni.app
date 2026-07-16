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
    public async Task DataStore_ReadsBoardIdCreatesUploadedFolderAndListsFiles()
    {
        var folder = CreateFolder("DAQ");
        var uploaded = CreateFolder("uploaded");
        var boardIdFile = CreateStorageFile("BOARDID", Encoding.ASCII.GetBytes("ABCDEF1234567890"));
        var older = CreateStorageFile("older.SST", TestSstFiles.CreateValidV3(timestamp: 100));
        var newer = CreateStorageFile("newer.SST", TestSstFiles.CreateValidV3(timestamp: 200));
        var invalid = CreateStorageFile("broken.SST", [0x01, 0x02]);
        invalid.OpenReadAsync().ThrowsAsync(new InvalidDataException("not telemetry"));
        var ignored = CreateStorageFile("notes.txt", Encoding.UTF8.GetBytes("not telemetry"));
        folder.GetItemsAsync().Returns(
            _ => EnumerateStorageItems(boardIdFile),
            _ => EnumerateStorageItems(uploaded, boardIdFile, older, invalid, ignored, newer));
        folder.CreateFolderAsync("uploaded").Returns(Task.FromResult<IStorageFolder?>(uploaded));

        var dataStore = new StorageProviderTelemetryDataStore(folder);
        await dataStore.Initialization;
        var files = await dataStore.GetFiles();

        Assert.Equal("DAQ", dataStore.Name);
        Assert.Equal(UuidUtil.CreateDeviceUuid("abcdef1234567890"), dataStore.BoardId);
        await folder.Received(1).CreateFolderAsync("uploaded");
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

    [Fact]
    public async Task TelemetryFile_ReadSourceOwnsTheExactLogicalBytes()
    {
        var bytes = TestSstFiles.CreateValidV3();
        var storageFile = CreateStorageFile("ride.SST", bytes);
        var telemetryFile = await StorageProviderTelemetryFile.CreateAsync(storageFile);

        using var source = await telemetryFile.ReadSourceAsync();

        Assert.Equal("ride.SST", source.FileName);
        Assert.Equal(bytes.Length, source.LogicalLength);
        Assert.Equal(bytes.Length, source.AllocatedCapacity);
        Assert.Equal(bytes, source.SstBytes.ToArray());
    }

    private static IStorageFolder CreateFolder(string name)
    {
        var folder = Substitute.For<IStorageFolder>();
        folder.Name.Returns(name);
        folder.Path.Returns(new Uri($"file:///tmp/{Guid.NewGuid():N}/{name}"));
        return folder;
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
