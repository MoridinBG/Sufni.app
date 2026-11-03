using System;
using System.Security.Cryptography;
using System.Text;
using Sufni.Telemetry;
using Sufni.App.Shared.Common;

namespace Sufni.App.Sessions.Models;

/// <summary>Identifies recorded content independently of its display name and compression.</summary>
public static class RecordedSessionSourceHash
{
    // Permanent namespace for session identities; changing it would duplicate recordings.
    private static readonly Guid SessionNamespace = new("58ef0951-9ae4-441f-92e8-81870715e6ff");

    public static Guid CreateSessionId(string sourceHash) =>
        UuidUtil.CreateVersion5(SessionNamespace, Encoding.UTF8.GetBytes(sourceHash));

    public static string ComputeImportedSst(ReadOnlySpan<byte> sstBytes) =>
        Convert.ToHexStringLower(SHA256.HashData(sstBytes));

    public static string Compute(RecordedSessionSourceKind sourceKind, ReadOnlyMemory<byte> payload)
    {
        if (sourceKind == RecordedSessionSourceKind.ImportedSst)
            return ComputeImportedSst(RecordedSessionSourcePayloadCodec.DecompressImportedSst(payload));
        if (sourceKind != RecordedSessionSourceKind.LiveCapture)
            throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unknown recorded session source kind.");

        var capture = TelemetryCodec.DecodeLiveSource(payload);
        // The live metadata name comes from the setup selected at capture time.
        capture = capture with { Metadata = capture.Metadata with { SourceName = string.Empty } };
        return Convert.ToHexStringLower(SHA256.HashData(TelemetryCodec.EncodeLiveSource(capture)));
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
