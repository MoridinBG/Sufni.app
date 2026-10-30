using System;
using Serilog;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Video.Services;

public sealed class Mp4VideoMetadataReader : IVideoMetadataReader
{
    private static readonly ILogger logger = Log.ForContext<Mp4VideoMetadataReader>();
    private const int StandardBoxHeaderSize = 8;
    private static readonly DateTimeOffset QuickTimeEpoch = new(1904, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<VideoMetadataReadResult> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!stream.CanSeek)
            {
                return new VideoMetadataReadResult.Invalid("Video metadata could not be read from a non-seekable stream.");
            }

            var movieHeader = await FindMovieHeaderAsync(stream, 0, stream.Length, cancellationToken);
            if (movieHeader is null)
            {
                return new VideoMetadataReadResult.Invalid("Video metadata did not include a QuickTime movie header.");
            }

            return await ParseMovieHeaderAsync(stream, movieHeader.Value.PayloadStart, movieHeader.Value.End, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is IOException
                                  or InvalidDataException
                                  or EndOfStreamException
                                  or NotSupportedException
                                  or ArgumentOutOfRangeException
                                  or OverflowException)
        {
            logger.Warning(e, "Video metadata could not be read");
            return new VideoMetadataReadResult.Invalid(e.Message);
        }
    }

    private static async Task<BoxHeader?> FindMovieHeaderAsync(
        Stream stream,
        long start,
        long end,
        CancellationToken cancellationToken)
    {
        stream.Position = start;

        while (stream.Position < end)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var header = await TryReadBoxHeaderAsync(stream, end, cancellationToken);
            if (header is null)
            {
                return null;
            }

            if (header.Value.Type == "mvhd")
            {
                return header;
            }

            if (header.Value.Type == "moov")
            {
                var nested = await FindMovieHeaderAsync(
                    stream,
                    header.Value.PayloadStart,
                    header.Value.End,
                    cancellationToken);
                if (nested is not null)
                {
                    return nested;
                }
            }

            stream.Position = header.Value.End;
        }

        return null;
    }

    private static async Task<BoxHeader?> TryReadBoxHeaderAsync(
        Stream stream,
        long parentEnd,
        CancellationToken cancellationToken)
    {
        var start = stream.Position;
        if (parentEnd - start < StandardBoxHeaderSize)
        {
            return null;
        }

        var header = new byte[StandardBoxHeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken);

        var size32 = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        var type = Encoding.ASCII.GetString(header.AsSpan(4, 4));
        long headerSize = StandardBoxHeaderSize;
        ulong size;

        if (size32 == 1)
        {
            var largeSize = new byte[8];
            await stream.ReadExactlyAsync(largeSize, cancellationToken);
            size = BinaryPrimitives.ReadUInt64BigEndian(largeSize);
            headerSize += 8;
        }
        else if (size32 == 0)
        {
            size = (ulong)(parentEnd - start);
        }
        else
        {
            size = size32;
        }

        if (size < (ulong)headerSize)
        {
            throw new InvalidDataException($"MP4 box '{type}' has an invalid size.");
        }

        var end = checked(start + (long)size);
        if (end > parentEnd)
        {
            throw new InvalidDataException($"MP4 box '{type}' extends outside its parent box.");
        }

        return new BoxHeader(type, start, headerSize, end);
    }

    private static async Task<VideoMetadataReadResult> ParseMovieHeaderAsync(
        Stream stream,
        long payloadStart,
        long payloadEnd,
        CancellationToken cancellationToken)
    {
        stream.Position = payloadStart;

        var fullBoxHeader = new byte[4];
        await stream.ReadExactlyAsync(fullBoxHeader, cancellationToken);
        var version = fullBoxHeader[0];

        ulong creationSeconds;
        uint timescale;
        ulong durationUnits;

        switch (version)
        {
            case 0:
            {
                EnsurePayloadLength(payloadStart, payloadEnd, 4 + 16);
                var buffer = new byte[16];
                await stream.ReadExactlyAsync(buffer, cancellationToken);
                creationSeconds = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4));
                timescale = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(8, 4));
                durationUnits = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(12, 4));
                break;
            }
            case 1:
            {
                EnsurePayloadLength(payloadStart, payloadEnd, 4 + 28);
                var buffer = new byte[28];
                await stream.ReadExactlyAsync(buffer, cancellationToken);
                creationSeconds = BinaryPrimitives.ReadUInt64BigEndian(buffer.AsSpan(0, 8));
                timescale = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(16, 4));
                durationUnits = BinaryPrimitives.ReadUInt64BigEndian(buffer.AsSpan(20, 8));
                break;
            }
            default:
                return new VideoMetadataReadResult.Invalid($"Video metadata used unsupported movie header version {version}.");
        }

        if (creationSeconds == 0)
        {
            return new VideoMetadataReadResult.Invalid("Video metadata did not include a valid creation time.");
        }

        if (timescale == 0)
        {
            return new VideoMetadataReadResult.Invalid("Video metadata did not include a valid time scale.");
        }

        if (durationUnits == 0)
        {
            return new VideoMetadataReadResult.Invalid("Video metadata did not include a positive duration.");
        }

        var durationSeconds = durationUnits / (double)timescale;
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            return new VideoMetadataReadResult.Invalid("Video metadata did not include a positive duration.");
        }

        var startUtc = QuickTimeEpoch.AddSeconds(creationSeconds);
        return new VideoMetadataReadResult.Parsed(new VideoMetadata(startUtc, TimeSpan.FromSeconds(durationSeconds)));
    }

    private static void EnsurePayloadLength(long payloadStart, long payloadEnd, long requiredBytes)
    {
        if (payloadEnd - payloadStart < requiredBytes)
        {
            throw new InvalidDataException("MP4 movie header is truncated.");
        }
    }

    private readonly record struct BoxHeader(string Type, long Start, long HeaderSize, long End)
    {
        public long PayloadStart => Start + HeaderSize;
    }
}
