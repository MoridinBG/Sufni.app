using System;

namespace Sufni.App.Acquisition.Services;

internal sealed class FramedMessageReader(
    int headerSize,
    FramedMessageReader.FrameTotalLengthReader readTotalLength)
{
    private readonly UnreadByteBuffer unreadBytes = new();

    public delegate int FrameTotalLengthReader(ReadOnlySpan<byte> headerBytes);

    public delegate TFrame? FrameParser<TFrame>(ReadOnlySpan<byte> frameBytes)
        where TFrame : class;

    public int BufferedByteCount => unreadBytes.BufferedByteCount;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        unreadBytes.Append(bytes);
    }

    public void Reset()
    {
        unreadBytes.Reset();
    }

    public bool TryReadFrame<TFrame>(FrameParser<TFrame> parseFrame, out TFrame? frame)
        where TFrame : class
    {
        while (true)
        {
            frame = null;
            if (unreadBytes.BufferedByteCount < headerSize)
            {
                return false;
            }

            var pendingSpan = unreadBytes.UnreadBytes;
            var totalLength = readTotalLength(pendingSpan[..headerSize]);
            if (totalLength < headerSize)
            {
                throw new FormatException("Frame total length is shorter than the frame header.");
            }

            if (unreadBytes.BufferedByteCount < totalLength)
            {
                return false;
            }

            frame = parseFrame(pendingSpan[..totalLength]);
            unreadBytes.Consume(totalLength);
            if (frame is not null)
            {
                return true;
            }
        }
    }
}
