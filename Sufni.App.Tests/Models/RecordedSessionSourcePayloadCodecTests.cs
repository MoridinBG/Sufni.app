using Sufni.App.Models;

namespace Sufni.App.Tests.Models;

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
}
