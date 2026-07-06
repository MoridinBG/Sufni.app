using Avalonia.Platform.Storage;
using NSubstitute;
using static Sufni.App.Tests.TestSupport.Fixtures.TestStorageItems;

using Sufni.App.Acquisition.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Acquisition.Models;

public class TelemetryFileInspectionMappingTests
{
    [Theory]
    [InlineData(TelemetryFileSourceKind.MassStorage, InspectionPayload.ValidV4WithUnknownChunk)]
    [InlineData(TelemetryFileSourceKind.StorageProvider, InspectionPayload.ValidV4WithUnknownChunk)]
    [InlineData(TelemetryFileSourceKind.MassStorage, InspectionPayload.MalformedV4)]
    [InlineData(TelemetryFileSourceKind.StorageProvider, InspectionPayload.MalformedV4)]
    [InlineData(TelemetryFileSourceKind.MassStorage, InspectionPayload.TrimmedV4)]
    [InlineData(TelemetryFileSourceKind.StorageProvider, InspectionPayload.TrimmedV4)]
    public async Task TelemetryFile_MapsInspectionResult_ForSourceAndPayload(
        TelemetryFileSourceKind sourceKind,
        InspectionPayload payload)
    {
        var file = await CreateTelemetryFileAsync(sourceKind, payload);

        Assert.Equal((byte)4, file.Version);
        Assert.False(file.ShouldBeImported);
        switch (payload)
        {
            case InspectionPayload.ValidV4WithUnknownChunk:
                Assert.True(file.HasUnknown);
                Assert.True(file.CanImport);
                Assert.Null(file.MalformedMessage);
                Assert.Equal("00:00:05", file.Duration);
                break;
            case InspectionPayload.MalformedV4:
                Assert.False(file.HasUnknown);
                Assert.False(file.CanImport);
                Assert.False(string.IsNullOrWhiteSpace(file.MalformedMessage));
                break;
            case InspectionPayload.TrimmedV4:
                Assert.True(file.CanImport);
                Assert.False(string.IsNullOrWhiteSpace(file.MalformedMessage));
                Assert.Equal("00:00:05", file.Duration);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(payload), payload, null);
        }
    }

    [Fact]
    public async Task StorageProviderTelemetryFile_OnImported_MovesToUploadedFolder()
    {
        var parent = Substitute.For<IStorageFolder>();
        var uploaded = Substitute.For<IStorageFolder>();
        uploaded.Name.Returns("uploaded");

        var storageFile = CreateStorageFile("sample.SST", TestSstFiles.CreateValidV4WithUnknownChunk(telemetrySampleCount: 5000));
        storageFile.GetParentAsync().Returns(Task.FromResult<IStorageFolder?>(parent));
        storageFile.MoveAsync(uploaded).Returns(Task.FromResult<IStorageItem?>(storageFile));
        parent.GetItemsAsync().Returns(EnumerateStorageItems(uploaded));

        var file = await StorageProviderTelemetryFile.CreateAsync(storageFile);

        await file.OnImported();

        Assert.True(file.Imported);
        await storageFile.Received(1).MoveAsync(uploaded);
    }

    [Fact]
    public async Task StorageProviderTelemetryFile_OnTrashed_Throws_WhenTrashFolderMissing()
    {
        var parent = Substitute.For<IStorageFolder>();
        var uploaded = Substitute.For<IStorageFolder>();
        uploaded.Name.Returns("uploaded");

        var storageFile = CreateStorageFile("sample.SST", TestSstFiles.CreateValidV4WithUnknownChunk(telemetrySampleCount: 5000));
        storageFile.GetParentAsync().Returns(Task.FromResult<IStorageFolder?>(parent));
        parent.GetItemsAsync().Returns(EnumerateStorageItems(uploaded));

        var file = await StorageProviderTelemetryFile.CreateAsync(storageFile);

        await Assert.ThrowsAsync<Exception>(() => file.OnTrashed());
        await storageFile.DidNotReceive().MoveAsync(Arg.Any<IStorageFolder>());
    }

    private static async Task<ITelemetryFile> CreateTelemetryFileAsync(
        TelemetryFileSourceKind sourceKind,
        InspectionPayload payload)
    {
        var bytes = payload switch
        {
            InspectionPayload.ValidV4WithUnknownChunk => TestSstFiles.CreateValidV4WithUnknownChunk(telemetrySampleCount: 5000),
            InspectionPayload.MalformedV4 => TestSstFiles.CreateMalformedV4WithInvalidTelemetryLength(),
            InspectionPayload.TrimmedV4 => TestSstFiles.CreateV4WithTelemetryChunkExtendingPastEnd(telemetrySampleCount: 5000),
            _ => throw new ArgumentOutOfRangeException(nameof(payload), payload, null)
        };
        var fileName = payload switch
        {
            InspectionPayload.MalformedV4 => "broken.SST",
            InspectionPayload.TrimmedV4 => "trimmed.SST",
            _ => "sample.SST"
        };

        if (sourceKind is TelemetryFileSourceKind.StorageProvider)
        {
            return await StorageProviderTelemetryFile.CreateAsync(CreateStorageFile(fileName, bytes));
        }

        using var tempDirectory = new TempDirectory("sufni-inspection-test");
        var path = Path.Combine(tempDirectory.Path, fileName);
        File.WriteAllBytes(path, bytes);
        return new MassStorageTelemetryFile(new FileInfo(path));
    }

    private static IStorageFile CreateStorageFile(string name, byte[] bytes)
    {
        var storageFile = Substitute.For<IStorageFile>();
        storageFile.Name.Returns(name);
        storageFile.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream(bytes)));
        return storageFile;
    }

    public enum TelemetryFileSourceKind
    {
        MassStorage,
        StorageProvider
    }

    public enum InspectionPayload
    {
        ValidV4WithUnknownChunk,
        MalformedV4,
        TrimmedV4
    }
}
