using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sufni.App.Infrastructure;

namespace Sufni.App.Sessions.Models;

/// <summary>Identifies recorded content independently of its display name and compression.</summary>
public static class RecordedSessionSourceHash
{
    // Permanent namespace for session identities; changing it would duplicate recordings.
    private static readonly Guid SessionNamespace = new("58ef0951-9ae4-441f-92e8-81870715e6ff");

    public static Guid CreateSessionId(string sourceHash)
    {
        var name = Encoding.UTF8.GetBytes(sourceHash);
        var input = new byte[16 + name.Length];
        SessionNamespace.TryWriteBytes(input.AsSpan(0, 16), bigEndian: true, out _);
        name.CopyTo(input, 16);
        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    public static string ComputeImportedSst(ReadOnlySpan<byte> sstBytes) =>
        Convert.ToHexStringLower(SHA256.HashData(sstBytes));

    public static string Compute(RecordedSessionSourceKind sourceKind, ReadOnlyMemory<byte> payload)
    {
        if (sourceKind == RecordedSessionSourceKind.ImportedSst)
            return ComputeImportedSst(RecordedSessionSourcePayloadCodec.DecompressImportedSst(payload));
        if (sourceKind != RecordedSessionSourceKind.LiveCapture)
            throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unknown recorded session source kind.");

        var capture = JsonSerializer.Deserialize(payload.Span, AppJson.Context.RecordedLiveCaptureSourcePayload)
            ?? throw new JsonException("Live capture source is missing.");
        // The live metadata name comes from the setup selected at capture time.
        capture.Metadata.SourceName = string.Empty;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AppJson.Serialize(capture))));
    }

    public static bool Matches(RecordedSessionSource source) =>
        !string.IsNullOrWhiteSpace(source.SourceName) &&
        !string.IsNullOrWhiteSpace(source.SourceHash) &&
        StringComparer.Ordinal.Equals(source.SourceHash, Compute(source.SourceKind, source.Payload));

    public static bool Matches(RecordedSessionSourcePayload source) =>
        !string.IsNullOrWhiteSpace(source.SourceName) &&
        !string.IsNullOrWhiteSpace(source.SourceHash) &&
        StringComparer.Ordinal.Equals(source.SourceHash, Compute(source.SourceKind, source.Payload));
}
