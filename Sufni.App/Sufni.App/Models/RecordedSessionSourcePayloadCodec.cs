using ZstdSharp;

namespace Sufni.App.Models;

public static class RecordedSessionSourcePayloadCodec
{
    private const int ImportedSstCompressionLevel = 3;

    public static byte[] CompressImportedSst(byte[] sstBytes)
    {
        using var compressor = new Compressor(ImportedSstCompressionLevel);
        return compressor.Wrap(sstBytes).ToArray();
    }

    public static byte[] DecompressImportedSst(byte[] payload)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(payload).ToArray();
    }
}
