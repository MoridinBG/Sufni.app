using Avalonia.Platform.Storage;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Models;

public class TelemetryFileInspectionMappingTests
{
    [Fact]
    public void MassStorageTelemetryFile_ValidV4WithUnknownChunk_SetsHasUnknownWithoutMalformed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.SST");

        try
        {
            File.WriteAllBytes(path, TestSstFiles.CreateValidV4WithUnknownChunk(telemetrySampleCount: 5000));

            var file = new MassStorageTelemetryFile(new FileInfo(path));

            Assert.Equal((byte)4, file.Version);
            Assert.True(file.HasUnknown);
            Assert.Null(file.MalformedMessage);
            Assert.True(file.CanImport);
            Assert.False(file.ShouldBeImported);
            Assert.Equal("00:00:05", file.Duration);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MassStorageTelemetryFile_MalformedV4_IsNotImportable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.SST");

        try
        {
            File.WriteAllBytes(path, TestSstFiles.CreateMalformedV4WithInvalidTelemetryLength());

            var file = new MassStorageTelemetryFile(new FileInfo(path));

            Assert.Equal((byte)4, file.Version);
            Assert.False(file.HasUnknown);
            Assert.False(file.CanImport);
            Assert.False(file.ShouldBeImported);
            Assert.False(string.IsNullOrWhiteSpace(file.MalformedMessage));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task StorageProviderTelemetryFile_ValidV4WithUnknownChunk_SetsHasUnknownWithoutMalformed()
    {
        var storageFile = Substitute.For<IStorageFile>();
        storageFile.Name.Returns("sample.SST");
        storageFile.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream(TestSstFiles.CreateValidV4WithUnknownChunk(telemetrySampleCount: 5000))));

        var file = await StorageProviderTelemetryFile.CreateAsync(storageFile);

        Assert.Equal((byte)4, file.Version);
        Assert.True(file.HasUnknown);
        Assert.Null(file.MalformedMessage);
        Assert.True(file.CanImport);
        Assert.False(file.ShouldBeImported);
        Assert.Equal("00:00:05", file.Duration);
    }

    [Fact]
    public async Task StorageProviderTelemetryFile_MalformedV4_IsNotImportable()
    {
        var storageFile = Substitute.For<IStorageFile>();
        storageFile.Name.Returns("broken.SST");
        storageFile.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream(TestSstFiles.CreateMalformedV4WithInvalidTelemetryLength())));

        var file = await StorageProviderTelemetryFile.CreateAsync(storageFile);

        Assert.Equal((byte)4, file.Version);
        Assert.False(file.HasUnknown);
        Assert.False(file.CanImport);
        Assert.False(file.ShouldBeImported);
        Assert.False(string.IsNullOrWhiteSpace(file.MalformedMessage));
    }

    [Fact]
    public void MassStorageTelemetryFile_TrimmedV4_IsImportableWithMalformedMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.SST");

        try
        {
            File.WriteAllBytes(path, TestSstFiles.CreateV4WithTelemetryChunkExtendingPastEnd(telemetrySampleCount: 5000));

            var file = new MassStorageTelemetryFile(new FileInfo(path));

            Assert.True(file.CanImport);
            Assert.False(file.ShouldBeImported);
            Assert.False(string.IsNullOrWhiteSpace(file.MalformedMessage));
            Assert.Equal("00:00:05", file.Duration);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task StorageProviderTelemetryFile_TrimmedV4_IsImportableWithMalformedMessage()
    {
        var storageFile = Substitute.For<IStorageFile>();
        storageFile.Name.Returns("trimmed.SST");
        storageFile.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream(TestSstFiles.CreateV4WithTelemetryChunkExtendingPastEnd(telemetrySampleCount: 5000))));

        var file = await StorageProviderTelemetryFile.CreateAsync(storageFile);

        Assert.True(file.CanImport);
        Assert.False(file.ShouldBeImported);
        Assert.False(string.IsNullOrWhiteSpace(file.MalformedMessage));
        Assert.Equal("00:00:05", file.Duration);
    }

    [Fact]
    public async Task StorageProviderTelemetryFile_OnImported_MovesToUploadedFolder()
    {
        var parent = Substitute.For<IStorageFolder>();
        var uploaded = Substitute.For<IStorageFolder>();
        uploaded.Name.Returns("uploaded");

        var storageFile = Substitute.For<IStorageFile>();
        storageFile.Name.Returns("sample.SST");
        storageFile.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream(TestSstFiles.CreateValidV4WithUnknownChunk(telemetrySampleCount: 5000))));
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

        var storageFile = Substitute.For<IStorageFile>();
        storageFile.Name.Returns("sample.SST");
        storageFile.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream(TestSstFiles.CreateValidV4WithUnknownChunk(telemetrySampleCount: 5000))));
        storageFile.GetParentAsync().Returns(Task.FromResult<IStorageFolder?>(parent));
        parent.GetItemsAsync().Returns(EnumerateStorageItems(uploaded));

        var file = await StorageProviderTelemetryFile.CreateAsync(storageFile);

        await Assert.ThrowsAsync<Exception>(() => file.OnTrashed());
        await storageFile.DidNotReceive().MoveAsync(Arg.Any<IStorageFolder>());
    }

    private static async IAsyncEnumerable<IStorageItem> EnumerateStorageItems(params IStorageItem[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
