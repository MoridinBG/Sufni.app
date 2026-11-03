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
    public async Task GetFiles_SkipsInaccessibleFilesAndRetainsMalformedRows()
    {
        var folder = CreateFolder("DAQ");
        var uploaded = CreateFolder("uploaded");
        var boardIdFile = BinaryFile("BOARDID", Encoding.ASCII.GetBytes("ABCDEF1234567890"));
        var older = BinaryFile("older.SST", TestSstFiles.CreateValidV3(timestamp: 100));
        var newer = BinaryFile("newer.SST", TestSstFiles.CreateValidV3(timestamp: 200));
        var invalid = BinaryFile("broken.SST", [0x01, 0x02]);
        var inaccessible = BinaryFile("inaccessible.SST", []);
        inaccessible.OpenReadAsync().ThrowsAsync(new IOException("device disconnected"));
        var ignored = BinaryFile("notes.txt", Encoding.UTF8.GetBytes("not telemetry"));
        folder.GetItemsAsync().Returns(
            _ => EnumerateStorageItems(boardIdFile),
            _ => EnumerateStorageItems(uploaded, boardIdFile, older, invalid, inaccessible, ignored, newer));
        folder.CreateFolderAsync("uploaded").Returns(Task.FromResult<IStorageFolder?>(uploaded));

        var dataStore = new StorageProviderTelemetryDataStore(folder);
        await dataStore.Initialization;
        var files = Assert.IsType<TelemetryFileListing.Listed>(await dataStore.GetFiles(TestContext.Current.CancellationToken)).Files;

        Assert.Collection(
            files,
            first => Assert.Equal("newer.SST", first.FileName),
            second => Assert.Equal("older.SST", second.FileName),
            malformed =>
            {
                Assert.Equal("broken.SST", malformed.FileName);
                Assert.False(malformed.CanImport);
                Assert.NotEmpty(malformed.MalformedMessage!);
            });
    }

    [Fact]
    public async Task TelemetryFile_ReadSourceOwnsTheExactLogicalBytes()
    {
        var bytes = TestSstFiles.CreateValidV3();
        var storageFile = BinaryFile("ride.SST", bytes);
        var telemetryFile = await StorageProviderTelemetryFile.CreateAsync(storageFile);

        using var source = await telemetryFile.ReadSourceAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ride.SST", source.FileName);
        Assert.Equal(bytes, source.SstBytes.ToArray());
    }

    private static IStorageFolder CreateFolder(string name)
    {
        var folder = Substitute.For<IStorageFolder>();
        folder.Name.Returns(name);
        folder.Path.Returns(new Uri($"file:///tmp/{Guid.NewGuid():N}/{name}"));
        return folder;
    }

}
