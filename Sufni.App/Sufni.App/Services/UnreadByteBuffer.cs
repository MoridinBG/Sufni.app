using System;

namespace Sufni.App.Services;

internal sealed class UnreadByteBuffer
{
    private byte[] buffer = [];
    private int unreadOffset;

    public int BufferedByteCount { get; private set; }

    public ReadOnlySpan<byte> UnreadBytes => buffer.AsSpan(unreadOffset, BufferedByteCount);

    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        EnsureWriteCapacity(bytes.Length);
        bytes.CopyTo(buffer.AsSpan(unreadOffset + BufferedByteCount, bytes.Length));
        BufferedByteCount += bytes.Length;
    }

    public void Reset()
    {
        buffer = [];
        unreadOffset = 0;
        BufferedByteCount = 0;
    }

    public void Consume(int length)
    {
        if ((uint)length > (uint)BufferedByteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        unreadOffset += length;
        BufferedByteCount -= length;
        if (BufferedByteCount == 0)
        {
            unreadOffset = 0;
        }
        else if (unreadOffset >= buffer.Length / 2)
        {
            CompactUnreadBytes();
        }
    }

    private void EnsureWriteCapacity(int appendLength)
    {
        var requiredLength = unreadOffset + BufferedByteCount + appendLength;
        if (buffer.Length >= requiredLength)
        {
            return;
        }

        CompactUnreadBytes();
        requiredLength = BufferedByteCount + appendLength;
        if (buffer.Length >= requiredLength)
        {
            return;
        }

        var newLength = buffer.Length == 0 ? 256 : buffer.Length;
        while (newLength < requiredLength)
        {
            newLength = checked(newLength * 2);
        }

        var resized = new byte[newLength];
        if (BufferedByteCount > 0)
        {
            buffer.AsSpan(unreadOffset, BufferedByteCount).CopyTo(resized);
        }

        buffer = resized;
        unreadOffset = 0;
    }

    private void CompactUnreadBytes()
    {
        if (unreadOffset == 0)
        {
            return;
        }

        if (BufferedByteCount == 0)
        {
            unreadOffset = 0;
            return;
        }

        buffer.AsSpan(unreadOffset, BufferedByteCount).CopyTo(buffer);
        unreadOffset = 0;
    }
}