using System;
using System.Collections;
using System.Collections.Generic;

namespace Sufni.App.Services.LiveStreaming;

/// <summary>
/// Fixed-capacity circular buffer that implements <see cref="IReadOnlyList{T}"/>
/// so consumers can index into the logical sequence without copying. When full,
/// the oldest element is silently overwritten.
/// </summary>
internal sealed class SlidingWindowBuffer<T>(int capacity) : IReadOnlyList<T>
{
    private readonly T[] buffer = new T[capacity];
    private int head;
    private int count;

    public int Count => count;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return buffer[(head + index) % buffer.Length];
        }
    }

    public void Append(T value)
    {
        var writeIndex = (head + count) % buffer.Length;
        buffer[writeIndex] = value;

        if (count < buffer.Length)
        {
            count++;
        }
        else
        {
            head = (head + 1) % buffer.Length;
        }
    }

    public void Clear()
    {
        head = 0;
        count = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
