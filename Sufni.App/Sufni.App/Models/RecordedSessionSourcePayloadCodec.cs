using ZstdSharp;

namespace Sufni.App.Models;

/// <summary>
/// Encodes and decodes raw-source payloads for persisted recorded sessions.
/// Imported SST files are stored compressed so the canonical raw artifact can
/// remain local without carrying the full file size in the hot metadata path.
/// </summary>
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
