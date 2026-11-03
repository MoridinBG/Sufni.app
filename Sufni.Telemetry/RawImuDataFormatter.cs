using System.Buffers;
using System.Collections.Immutable;
using MessagePack;
using MessagePack.Formatters;

namespace Sufni.Telemetry;

public sealed class RawImuDataFormatter : IMessagePackFormatter<RawImuData?>
{
    private const int CompactEncodingVersion = 1;

    public void Serialize(
        ref MessagePackWriter writer,
        RawImuData? value,
        MessagePackSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        writer.WriteArrayHeader(6);
        writer.Write(CompactEncodingVersion);
        writer.Write(value.SampleRate);
        WriteCompactMeta(ref writer, value.Meta);
        var segments = value.SampleSegments;
        WriteCompactActiveLocations(ref writer, value.ActiveLocations, segments);
        WriteCompactSegments(ref writer, segments);
        writer.Write(value.HasGaps);
    }

    private static void WriteCompactMeta(
        ref MessagePackWriter writer,
        IReadOnlyList<ImuMetaEntry> meta)
    {
        writer.WriteArrayHeader(meta.Count);
        foreach (var entry in meta)
        {
            writer.WriteArrayHeader(3);
            writer.Write(entry.LocationId);
            writer.Write(entry.AccelLsbPerG);
            writer.Write(entry.GyroLsbPerDps);
        }
    }

    private static void WriteCompactActiveLocations(
        ref MessagePackWriter writer,
        ImmutableArray<byte> activeLocations,
        ImuSampleSegmentCollection segments)
    {
        if (activeLocations.Length > 0)
        {
            writer.Write(activeLocations.AsSpan());
            return;
        }

        Span<byte> derivedLocations = stackalloc byte[byte.MaxValue + 1];
        var count = 0;
        foreach (var segment in segments)
        {
            if (!derivedLocations[..count].Contains(segment.LocationId))
            {
                derivedLocations[count++] = segment.LocationId;
            }
        }

        writer.Write(derivedLocations[..count]);
    }

    private static void WriteCompactSegments(
        ref MessagePackWriter writer,
        ImuSampleSegmentCollection segments)
    {
        writer.WriteArrayHeader(segments.Count);
        foreach (var segment in segments)
        {
            writer.WriteArrayHeader(4);
            writer.Write(segment.LocationId);
            writer.Write(segment.FirstIndex);
            writer.Write(segment.FirstMonotonicDeltaUs);
            writer.WriteArrayHeader(segment.Count);
            foreach (var sample in segment)
            {
                writer.WriteArrayHeader(6);
                writer.Write(sample.Ax);
                writer.Write(sample.Ay);
                writer.Write(sample.Az);
                writer.Write(sample.Gx);
                writer.Write(sample.Gy);
                writer.Write(sample.Gz);
            }
        }
    }

    public RawImuData? Deserialize(
        ref MessagePackReader reader,
        MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        options.Security.DepthStep(ref reader);
        try
        {
            return reader.NextMessagePackType switch
            {
                MessagePackType.Map => DeserializeLegacy(ref reader, options),
                MessagePackType.Array => DeserializeCompact(ref reader),
                _ => throw new InvalidDataException("IMU data must be a legacy map or a compact array."),
            };
        }
        finally
        {
            reader.Depth--;
        }
    }

    private static RawImuData DeserializeLegacy(
        ref MessagePackReader reader,
        MessagePackSerializerOptions options)
    {
        ImmutableArray<ImuMetaEntry> meta = [];
        ImmutableArray<ImuRecord> records = [];
        ImmutableArray<byte> activeLocations = [];
        ImmutableArray<RawImuSegment> segments = [];
        var sampleRate = 0;
        var hasGaps = false;
        var fieldCount = reader.ReadMapHeader();
        for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            var fieldName = reader.ReadString();
            switch (fieldName)
            {
                case nameof(RawImuData.Meta):
                    meta = options.Resolver.GetFormatterWithVerify<ImmutableArray<ImuMetaEntry>>()
                        .Deserialize(ref reader, options)!;
                    break;
                case nameof(RawImuData.SampleRate):
                    sampleRate = reader.ReadInt32();
                    break;
                case nameof(RawImuData.Records):
                    records = options.Resolver.GetFormatterWithVerify<ImmutableArray<ImuRecord>>()
                        .Deserialize(ref reader, options)!;
                    break;
                case nameof(RawImuData.ActiveLocations):
                    activeLocations = options.Resolver.GetFormatterWithVerify<ImmutableArray<byte>>()
                        .Deserialize(ref reader, options)!;
                    break;
                case nameof(RawImuData.Segments):
                    segments = options.Resolver.GetFormatterWithVerify<ImmutableArray<RawImuSegment>>()
                        .Deserialize(ref reader, options)!;
                    break;
                case nameof(RawImuData.HasGaps):
                    hasGaps = reader.ReadBoolean();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new RawImuData { Meta = meta, Records = records, ActiveLocations = activeLocations, Segments = segments, SampleRate = sampleRate, HasGaps = hasGaps };
    }

    private static RawImuData DeserializeCompact(ref MessagePackReader reader)
    {
        var fieldCount = reader.ReadArrayHeader();
        if (fieldCount < 6)
        {
            throw new InvalidDataException("Compact IMU data must contain six fields.");
        }

        var encodingVersion = reader.ReadInt32();
        if (encodingVersion != CompactEncodingVersion)
        {
            throw new InvalidDataException($"Unsupported compact IMU encoding version {encodingVersion}.");
        }

        var result = new RawImuData
        {
            SampleRate = reader.ReadInt32(),
            Meta = [.. ReadCompactMeta(ref reader)],
            ActiveLocations = [.. ReadCompactActiveLocations(ref reader)],
            Segments = [.. ReadCompactSegments(ref reader)],
            HasGaps = reader.ReadBoolean(),
        };

        for (var fieldIndex = 6; fieldIndex < fieldCount; fieldIndex++)
        {
            reader.Skip();
        }

        return result;
    }

    private static List<ImuMetaEntry> ReadCompactMeta(ref MessagePackReader reader)
    {
        if (reader.TryReadNil())
        {
            return [];
        }

        var count = reader.ReadArrayHeader();
        var result = new List<ImuMetaEntry>(count);
        for (var index = 0; index < count; index++)
        {
            var fieldCount = reader.ReadArrayHeader();
            if (fieldCount < 3)
            {
                throw new InvalidDataException("Compact IMU metadata must contain three fields.");
            }

            result.Add(new ImuMetaEntry(reader.ReadByte(), reader.ReadSingle(), reader.ReadSingle()));
            SkipRemaining(ref reader, fieldCount, 3);
        }
        return result;
    }

    private static List<byte> ReadCompactActiveLocations(ref MessagePackReader reader)
    {
        if (reader.TryReadNil())
        {
            return [];
        }

        if (reader.NextMessagePackType == MessagePackType.Binary)
        {
            var bytes = reader.ReadBytes();
            return bytes is null ? [] : [.. bytes.Value.ToArray()];
        }

        var count = reader.ReadArrayHeader();
        var result = new List<byte>(count);
        for (var index = 0; index < count; index++)
        {
            result.Add(reader.ReadByte());
        }
        return result;
    }

    private static List<RawImuSegment> ReadCompactSegments(ref MessagePackReader reader)
    {
        if (reader.TryReadNil())
        {
            return [];
        }

        var count = reader.ReadArrayHeader();
        var result = new List<RawImuSegment>(count);
        for (var index = 0; index < count; index++)
        {
            var fieldCount = reader.ReadArrayHeader();
            if (fieldCount < 4)
            {
                throw new InvalidDataException("Compact IMU segments must contain four fields.");
            }

            result.Add(new RawImuSegment
            {
                LocationId = reader.ReadByte(),
                FirstIndex = reader.ReadUInt64(),
                FirstMonotonicDeltaUs = reader.ReadUInt64(),
                Records = [.. ReadCompactSamples(ref reader)],
            });
            SkipRemaining(ref reader, fieldCount, 4);
        }
        return result;
    }

    private static ImuRecord[] ReadCompactSamples(ref MessagePackReader reader)
    {
        if (reader.TryReadNil())
        {
            return [];
        }

        var count = reader.ReadArrayHeader();
        var result = new ImuRecord[count];
        for (var index = 0; index < count; index++)
        {
            var fieldCount = reader.ReadArrayHeader();
            if (fieldCount < 6)
            {
                throw new InvalidDataException("Compact IMU samples must contain six fields.");
            }

            result[index] = new ImuRecord(
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16());
            SkipRemaining(ref reader, fieldCount, 6);
        }
        return result;
    }

    private static void SkipRemaining(ref MessagePackReader reader, int fieldCount, int consumed)
    {
        for (var fieldIndex = consumed; fieldIndex < fieldCount; fieldIndex++)
        {
            reader.Skip();
        }
    }
}
