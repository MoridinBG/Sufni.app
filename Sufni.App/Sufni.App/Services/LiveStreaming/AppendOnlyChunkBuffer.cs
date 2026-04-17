using System;
using System.Collections.Generic;

namespace Sufni.App.Services.LiveStreaming;

// Append-only chunk storage used by live capture so save/stat snapshotting can capture
// sealed chunk references plus the active-chunk boundary under lock and flatten later.
internal sealed class AppendOnlyChunkBuffer<T>
{
    private readonly int chunkSize;
    private readonly List<T[]> sealedChunks = [];
    private T[] activeChunk;
    private int activeCount;

    public AppendOnlyChunkBuffer(int chunkSize)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        this.chunkSize = chunkSize;
        activeChunk = new T[chunkSize];
    }

    public int Count { get; private set; }

    public void Append(T item)
    {
        if (activeCount == activeChunk.Length)
        {
            sealedChunks.Add(activeChunk);
            activeChunk = new T[chunkSize];
            activeCount = 0;
        }

        activeChunk[activeCount++] = item;
        Count++;
    }

    public void AppendRange(IReadOnlyList<T> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            Append(items[index]);
        }
    }

    public void Clear()
    {
        sealedChunks.Clear();
        activeChunk = new T[chunkSize];
        activeCount = 0;
        Count = 0;
    }

    public ChunkedBufferSnapshot<T> CreateSnapshot()
    {
        return new ChunkedBufferSnapshot<T>([.. sealedChunks], activeChunk, activeCount, Count);
    }
}

internal readonly record struct ChunkedBufferSnapshot<T>(
    T[][] SealedChunks,
    T[] ActiveChunk,
    int ActiveCount,
    int Count)
{
    public T[] ToArray()
    {
        if (Count == 0)
        {
            return [];
        }

        var values = new T[Count];
        var offset = 0;

        foreach (var chunk in SealedChunks)
        {
            Array.Copy(chunk, 0, values, offset, chunk.Length);
            offset += chunk.Length;
        }

        if (ActiveCount > 0)
        {
            Array.Copy(ActiveChunk, 0, values, offset, ActiveCount);
        }

        return values;
    }
}