using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Sufni.App.Models;

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
}
