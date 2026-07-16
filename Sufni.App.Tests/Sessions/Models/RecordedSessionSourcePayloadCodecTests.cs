
using Sufni.App.Sessions.Models;
namespace Sufni.App.Tests.Sessions.Models;

public class RecordedSessionSourcePayloadCodecTests
{
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
