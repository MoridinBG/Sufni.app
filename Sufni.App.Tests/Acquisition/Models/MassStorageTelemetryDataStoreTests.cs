
using Sufni.App.Acquisition.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Acquisition.Models;

public class MassStorageTelemetryDataStoreTests
{
    [Fact]
    public async Task CreateAsync_ReadsBoardIdAndCreatesUploadedFolder()
    {
        using var tempDirectory = new TempDirectory("sufni-mass-storage-test");
        File.WriteAllText(Path.Combine(tempDirectory.Path, "BOARDID"), "ABCDEF1234567890");
        var driveInfo = new DriveInfo(tempDirectory.Path);

        var dataStore = await MassStorageTelemetryDataStore.CreateAsync(driveInfo);

        Assert.Equal(UuidUtil.CreateDeviceUuid("abcdef1234567890"), dataStore.BoardId);
        Assert.True(Directory.Exists(Path.Combine(tempDirectory.Path, "uploaded")));
        Assert.Same(driveInfo, dataStore.DriveInfo);
        Assert.Contains(driveInfo.RootDirectory.Name, dataStore.Name);
    }

    [Fact]
    public async Task GetFiles_ReturnsValidSstFilesOrderedByStartTimeDescending()
    {
        using var tempDirectory = new TempDirectory("sufni-mass-storage-test");
        File.WriteAllText(Path.Combine(tempDirectory.Path, "BOARDID"), "0011223344556677");
        File.WriteAllBytes(
            Path.Combine(tempDirectory.Path, "older.SST"),
            TestSstFiles.CreateValidV3(timestamp: 100));
        File.WriteAllBytes(
            Path.Combine(tempDirectory.Path, "newer.SST"),
            TestSstFiles.CreateValidV3(timestamp: 200));
        File.WriteAllText(
            Path.Combine(tempDirectory.Path, "broken.SST"),
            "not telemetry");
        File.WriteAllText(
            Path.Combine(tempDirectory.Path, "notes.txt"),
            "ignored");
        var dataStore = await MassStorageTelemetryDataStore.CreateAsync(new DriveInfo(tempDirectory.Path));

        var files = await dataStore.GetFiles();

        Assert.Collection(
            files,
            first => Assert.Equal("newer.SST", first.FileName),
            second => Assert.Equal("older.SST", second.FileName));
    }

    [Fact]
    public async Task GetFiles_ReadSourceAsyncReturnsOriginalBytes()
    {
        using var tempDirectory = new TempDirectory("sufni-mass-storage-test");
        File.WriteAllText(Path.Combine(tempDirectory.Path, "BOARDID"), "0011223344556677");
        var sourceBytes = TestSstFiles.CreateValidV3(timestamp: 123);
        File.WriteAllBytes(Path.Combine(tempDirectory.Path, "sample.SST"), sourceBytes);
        var dataStore = await MassStorageTelemetryDataStore.CreateAsync(new DriveInfo(tempDirectory.Path));

        var file = Assert.Single(await dataStore.GetFiles());
        var source = await file.ReadSourceAsync();

        Assert.Equal("sample.SST", source.FileName);
        Assert.Equal(sourceBytes, source.SstBytes);
    }
}
