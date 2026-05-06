using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Sufni.App.Models;

/// <summary>
/// Computes the stable content hash for a recorded source.
/// The hash covers source kind, source name, schema version, and payload bytes
/// so source identity changes and raw-data changes are both visible.
/// </summary>
public static class RecordedSessionSourceHash
{
    public static string Compute(
        RecordedSessionSourceKind sourceKind,
        string sourceName,
        int schemaVersion,
        byte[] payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(sourceKind.ToStorageValue());
            writer.Write(sourceName);
            writer.Write(schemaVersion);
            writer.Write(payload.Length);
            writer.Write(payload);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    public static bool Matches(RecordedSessionSource source) =>
        !string.IsNullOrWhiteSpace(source.SourceName) &&
        !string.IsNullOrWhiteSpace(source.SourceHash) &&
        StringComparer.Ordinal.Equals(
            source.SourceHash,
            Compute(source.SourceKind, source.SourceName, source.SchemaVersion, source.Payload));

    public static bool Matches(RecordedSessionSourceTransfer source) =>
        !string.IsNullOrWhiteSpace(source.SourceName) &&
        !string.IsNullOrWhiteSpace(source.SourceHash) &&
        StringComparer.Ordinal.Equals(
            source.SourceHash,
            Compute(source.SourceKind, source.SourceName, source.SchemaVersion, source.Payload));
}
