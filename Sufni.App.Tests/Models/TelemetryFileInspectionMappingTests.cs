using System.Text;
using Avalonia.Platform.Storage;
using NSubstitute;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Models;

public class TelemetryFileInspectionMappingTests
{
    [Fact]
    public void MassStorageTelemetryFile_ValidV4WithUnknownChunk_SetsHasUnknownWithoutMalformed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.SST");

        try
        {
            File.WriteAllBytes(path, CreateValidV4WithUnknownChunk(telemetrySampleCount: 5000));

            var file = new MassStorageTelemetryFile(new FileInfo(path));

            Assert.False(file.Malformed);
            Assert.True(file.HasUnknown);
            Assert.Null(file.MalformedMessage);
            Assert.True(file.ShouldBeImported);
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
            File.WriteAllBytes(path, CreateMalformedV4WithInvalidTelemetryLength());

            var file = new MassStorageTelemetryFile(new FileInfo(path));

            Assert.True(file.Malformed);
            Assert.False(file.HasUnknown);
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
        storageFile.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream(CreateValidV4WithUnknownChunk(telemetrySampleCount: 5000))));

        var file = await StorageProviderTelemetryFile.CreateAsync(storageFile);

        Assert.False(file.Malformed);
        Assert.True(file.HasUnknown);
        Assert.Null(file.MalformedMessage);
        Assert.True(file.ShouldBeImported);
        Assert.Equal("00:00:05", file.Duration);
    }

    [Fact]
    public async Task StorageProviderTelemetryFile_MalformedV4_IsNotImportable()
    {
        var storageFile = Substitute.For<IStorageFile>();
        storageFile.Name.Returns("broken.SST");
        storageFile.OpenReadAsync().Returns(_ => Task.FromResult<Stream>(new MemoryStream(CreateMalformedV4WithInvalidTelemetryLength())));

        var file = await StorageProviderTelemetryFile.CreateAsync(storageFile);

        Assert.True(file.Malformed);
        Assert.False(file.HasUnknown);
        Assert.False(file.ShouldBeImported);
        Assert.False(string.IsNullOrWhiteSpace(file.MalformedMessage));
    }

    private static byte[] CreateValidV4WithUnknownChunk(int telemetrySampleCount)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0);
        writer.Write((long)123456789);

        writer.Write((byte)TlvChunkType.Rates);
        writer.Write((ushort)3);
        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)1000);

        writer.Write((byte)0xFF);
        writer.Write((ushort)2);
        writer.Write(new byte[] { 1, 2 });

        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)(telemetrySampleCount * 4));
        for (var i = 0; i < telemetrySampleCount; i++)
        {
            writer.Write((ushort)500);
            writer.Write((ushort)600);
        }

        return ms.ToArray();
    }

    private static byte[] CreateMalformedV4WithInvalidTelemetryLength()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0);
        writer.Write((long)123456789);

        writer.Write((byte)TlvChunkType.Rates);
        writer.Write((ushort)3);
        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)1000);

        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)5);
        writer.Write(new byte[] { 1, 2, 3, 4, 5 });

        return ms.ToArray();
    }
}