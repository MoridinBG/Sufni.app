using System;
using System.IO;
using System.IO.Compression;

namespace Sufni.App.Sessions.Models;

/// <summary>
/// Encodes and decodes raw-source payloads for persisted recorded sessions.
/// Imported SST files are stored compressed so the canonical raw artifact can
/// remain local without carrying the full file size in the hot metadata path.
/// </summary>
public static class RecordedSessionSourcePayloadCodec
{
    public static byte[] CompressImportedSst(byte[] sstBytes) =>
        CompressImportedSst((ReadOnlyMemory<byte>)sstBytes);

    // One-shot compression writes the content size into the frame header;
    // earlier app versions on paired devices need it to decode the payload.
    public static byte[] CompressImportedSst(ReadOnlyMemory<byte> sstBytes)
    {
        var payload = new byte[ZstandardEncoder.GetMaxCompressedLength(sstBytes.Length)];
        if (!ZstandardEncoder.TryCompress(sstBytes.Span, payload, out var written))
            throw new InvalidOperationException("Zstandard compression exceeded its maximum compressed length.");

        Array.Resize(ref payload, written);
        return payload;
    }

    /// <exception cref="InvalidDataException">The payload is not a complete Zstandard frame.</exception>
    public static byte[] DecompressImportedSst(ReadOnlyMemory<byte> payload)
    {
        if (!ZstandardDecoder.TryGetMaxDecompressedLength(payload.Span, out var maxLength) || maxLength > Array.MaxLength)
            throw new InvalidDataException("The imported SST payload has no valid Zstandard frame header.");

        var sstBytes = new byte[maxLength];
        if (!ZstandardDecoder.TryDecompress(payload.Span, sstBytes, out var written))
            throw new InvalidDataException("The imported SST payload is not a complete Zstandard frame.");

        Array.Resize(ref sstBytes, written);
        return sstBytes;
    }
}
