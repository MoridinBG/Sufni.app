using Sufni.App.Acquisition.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Acquisition.Models;

public class MassStorageTelemetryDataStoreTests
{
    [Fact]
    public async Task DataStore_ReadsBoardIdCreatesUploadedFolderListsFilesAndReadsSource()
    {
        using var tempDirectory = new TempDirectory("sufni-mass-storage-test");
        File.WriteAllText(Path.Combine(tempDirectory.Path, "BOARDID"), "ABCDEF1234567890");
        var olderBytes = TestSstFiles.CreateValidV3(timestamp: 100);
        var newerBytes = TestSstFiles.CreateValidV3(timestamp: 200);
        File.WriteAllBytes(Path.Combine(tempDirectory.Path, "older.SST"), olderBytes);
        File.WriteAllBytes(Path.Combine(tempDirectory.Path, "newer.SST"), newerBytes);
        File.WriteAllText(Path.Combine(tempDirectory.Path, "broken.SST"), "not telemetry");
        File.WriteAllText(Path.Combine(tempDirectory.Path, "notes.txt"), "ignored");
        var driveInfo = new DriveInfo(tempDirectory.Path);

        var dataStore = await MassStorageTelemetryDataStore.CreateAsync(driveInfo);
        var files = await dataStore.GetFiles();
        using var source = await files[0].ReadSourceAsync();

        Assert.Equal(UuidUtil.CreateDeviceUuid("abcdef1234567890"), dataStore.BoardId);
        Assert.True(Directory.Exists(Path.Combine(tempDirectory.Path, "uploaded")));
        Assert.Same(driveInfo, dataStore.DriveInfo);
        Assert.Contains(driveInfo.RootDirectory.Name, dataStore.Name);
        Assert.Collection(
            files,
            first => Assert.Equal("newer.SST", first.FileName),
            second => Assert.Equal("older.SST", second.FileName));
        Assert.Equal("newer.SST", source.FileName);
        Assert.Equal(newerBytes, source.SstBytes.ToArray());
    }
}
