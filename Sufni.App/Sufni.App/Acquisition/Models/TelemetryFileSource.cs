using System;
using System.IO;
using System.Threading;

namespace Sufni.App.Acquisition.Models;

/// <summary>
/// Owns the raw bytes and display name read from an importable telemetry file.
/// The logical range represents the canonical file content before it is
/// persisted as a recorded-session source.
/// </summary>
public sealed class TelemetryFileSource : IDisposable
{
    private byte[]? backingBytes;
    private readonly int offset;
    private readonly int logicalLength;

    public TelemetryFileSource(string fileName, byte[] sstBytes)
        : this(fileName, sstBytes, 0, sstBytes.Length)
    {
    }

    private TelemetryFileSource(
        string fileName,
        byte[] backingBytes,
        int offset,
        int logicalLength)
    {
        FileName = fileName;
        this.backingBytes = backingBytes;
        this.offset = offset;
        this.logicalLength = logicalLength;
    }

    public string FileName { get; }

    public ReadOnlyMemory<byte> SstBytes
    {
        get
        {
            var bytes = Volatile.Read(ref backingBytes)
                ?? throw new ObjectDisposedException(nameof(TelemetryFileSource));
            return bytes.AsMemory(offset, logicalLength);
        }
    }

    internal static TelemetryFileSource TakeOwnership(string fileName, MemoryStream memory)
    {
        if (!memory.TryGetBuffer(out var buffer))
        {
            throw new InvalidOperationException("The source buffer must expose its backing array.");
        }

        return new TelemetryFileSource(
            fileName,
            buffer.Array!,
            buffer.Offset,
            checked((int)memory.Length));
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref backingBytes, null);
    }
}
