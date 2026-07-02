namespace Sufni.Telemetry;

public sealed class SstV5SessionDescriptor
{
    public byte BoardId { get; init; }
    public uint AcceptedStreamMask { get; init; }
    public IReadOnlyList<SstV5StreamDescriptor> StreamDescriptors { get; init; } = [];
    public IReadOnlyList<SstV5OmissionRecord> OmissionRecords { get; init; } = [];

    public bool TryGetStream(byte streamKind, out SstV5StreamDescriptor descriptor)
    {
        foreach (var streamDescriptor in StreamDescriptors)
        {
            if (streamDescriptor.StreamKind == streamKind)
            {
                descriptor = streamDescriptor;
                return true;
            }
        }

        descriptor = null!;
        return false;
    }
}

public sealed class SstV5StreamDescriptor
{
    public byte StreamKind { get; init; }
    public byte TimingModelId { get; init; }
    public byte SourceDescriptorCount { get; init; }
    public uint AcceptedSensorMask { get; init; }
    public uint AcceptedExtensionMask { get; init; }
    public uint AcceptedRateMhz { get; init; }
    public uint AcceptedBatchDurationMs { get; init; }
    public ushort CompactPayloadRecordBytes { get; init; }
    public byte GpsDriverId { get; init; }
    public byte GpsPayloadEncodingId { get; init; }
    public ushort GpsPayloadRecordBytes { get; init; }
    public ushort GpsExtensionRecordBytes { get; init; }
    public IReadOnlyList<SstV5SourceDescriptor> Sources { get; init; } = [];
}

public sealed class SstV5SourceDescriptor
{
    public byte StreamKind { get; init; }
    public byte DriverId { get; init; }
    public byte PayloadEncodingId { get; init; }
    public byte PayloadValueCount { get; init; }
    public uint SourceBitMask { get; init; }
    public ushort PayloadByteOffset { get; init; }
    public ushort PayloadRecordBytes { get; init; }
    public ushort PayloadValueWidthBits { get; init; }
    public ushort CountMin { get; init; }
    public ushort CountMax { get; init; }
    public ushort WrapModulus { get; init; }
    public byte Wraps { get; init; }
    public float AccelLsbPerG { get; init; }
    public float GyroLsbPerDps { get; init; }
    public byte BikeFrameId { get; init; }
    public float TemperatureLsbPerCelsius { get; init; }
    public float TemperatureCelsiusAtRawZero { get; init; }
}

public readonly record struct SstV5OmissionRecord(
    byte TargetKind,
    byte StreamKind,
    byte AdmissionReason,
    uint TargetMask);

public readonly record struct SstV5DataHeader(
    ulong FirstIndex,
    ulong FirstMonotonicDeltaUs,
    uint SampleCount,
    uint ValidityMask);

public readonly record struct SstV5DecodedTravelRecord(
    uint SourceBitMask,
    ulong Index,
    ulong MonotonicDeltaUs,
    ushort Count);

public readonly record struct SstV5DecodedImuRecord(
    uint SourceBitMask,
    byte LocationId,
    ulong Index,
    ulong MonotonicDeltaUs,
    ImuRecord Record);

public readonly record struct SstV5DecodedTemperatureRecord(
    uint SourceBitMask,
    byte LocationId,
    ulong Index,
    ulong MonotonicDeltaUs,
    TemperatureSample Sample);

public readonly record struct SstV5DecodedGpsRecord(
    ulong Index,
    ulong MonotonicDeltaUs,
    GpsRecord Record);

public readonly record struct SstV5DecodedBatteryRecord(
    ulong Index,
    ulong MonotonicDeltaUs,
    ushort Millivolts,
    ushort Flags);

public readonly record struct SstV5DecodedMarkerRecord(
    ulong Index,
    ulong MonotonicDeltaUs,
    byte MarkerType);
