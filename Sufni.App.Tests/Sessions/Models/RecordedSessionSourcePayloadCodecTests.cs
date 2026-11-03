
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.Telemetry.Tests;
namespace Sufni.App.Tests.Sessions.Models;

public class RecordedSessionSourcePayloadCodecTests
{
    [Fact]
    public async Task LiveCapturePayload_PreservesFrozenJsonBytes()
    {
        var expected = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Codec", "live-capture-v1.json"),
            TestContext.Current.CancellationToken);
        var source = RecordedSessionSourceFactory.CreateLiveCapture(TelemetryCodecFixtureData.LiveCapture());

        Assert.Equal(expected, source.Payload);
        Assert.Equal("8d48096e0b4ef640b5d38f908494629944ee9bb64cefbce12a36828dcaba563a", source.SourceHash);
    }

    [Fact]
    public void ImportedSstPayload_RoundTripsThroughZstdCompression()
    {
        var source = Enumerable.Range(0, 8192)
            .Select(index => (byte)(index % 32))
            .ToArray();

        var compressed = RecordedSessionSourcePayloadCodec.CompressImportedSst(source);
        var decompressed = RecordedSessionSourcePayloadCodec.DecompressImportedSst(compressed);

        Assert.True(compressed.Length < source.Length);
        Assert.Equal(source, decompressed);
    }

    [Fact]
    public void ImportedSstPayload_CompressesOnlyTheLogicalMemoryRange()
    {
        var backingBytes = new byte[] { 99, 1, 2, 3, 88 };

        var compressed = RecordedSessionSourcePayloadCodec.CompressImportedSst(
            backingBytes.AsMemory(1, 3));
        var decompressed = RecordedSessionSourcePayloadCodec.DecompressImportedSst(compressed);

        Assert.Equal(new byte[] { 1, 2, 3 }, decompressed);
    }
}
