using System.Runtime.InteropServices;
using System.Text;

namespace Sufni.Telemetry;

public class RawTelemetryData
{
    #region Public properties

    public byte Version { get; set; }
    public ushort SampleRate { get; set; }
    public long Timestamp { get; set; }
    public ushort[] Front { get; set; } = [];
    public ushort[] Rear { get; set; } = [];
    public MarkerData[] Markers { get; set; } = [];
    public RawImuData? ImuData { get; set; }
    public GpsRecord[]? GpsData { get; set; }
    public TemperatureSample[] TemperatureData { get; set; } = [];
    public long SessionStartUtcMs { get; set; }
    public double? RecordingDurationSeconds { get; set; }
    public RawCountSegment[] FrontSegments { get; set; } = [];
    public RawCountSegment[] RearSegments { get; set; } = [];
    public RawStreamGap[] StreamGaps { get; set; } = [];
    public SstFinalStatus? FinalStatus { get; set; }
    public bool MissingFinalStatus { get; set; }

    #endregion Public properties

    #region Initializers

    public static SstFileInspection InspectStream(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        var (parser, version) = CreateParser(reader);
        return parser.Inspect(reader, version);
    }

    public static SstFileInspection InspectByteArray(byte[] bytes)
    {
        return InspectStream(new MemoryStream(bytes));
    }

    public static RawTelemetryData FromStream(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        var (parser, version) = CreateParser(reader);
        return parser.Parse(reader, version);
    }

    public static RawTelemetryData FromByteArray(byte[] bytes)
    {
        return FromMemory(bytes);
    }

    public static RawTelemetryData FromMemory(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Span.StartsWith("SST5"u8))
        {
            return new SstV5Parser().Parse(bytes[4..], SstV5ProtocolConstants.Version);
        }

        using var stream = CreateReadOnlyMemoryStream(bytes);
        return FromStream(stream);
    }

    public RawTelemetryData Slice(double startSeconds, double? endSeconds)
    {
        return TelemetrySlicer.Slice(this, startSeconds, endSeconds);
    }

    private static (ISstParser Parser, byte Version) CreateParser(BinaryReader reader)
    {
        var magic = reader.ReadBytes(4);
        if (magic.Length != 4)
        {
            throw new FormatException("Data is not SST format");
        }

        if (magic is [(byte)'S', (byte)'S', (byte)'T', (byte)'5'])
        {
            return (new SstV5Parser(), SstV5ProtocolConstants.Version);
        }

        if (magic[0] != (byte)'S' || magic[1] != (byte)'S' || magic[2] != (byte)'T')
        {
            throw new FormatException("Data is not SST format");
        }

        var version = magic[3];

        ISstParser parser = version switch
        {
            3 => new SstV3Parser(),
            4 => new SstV4TlvParser(),
            _ => throw new FormatException($"Unsupported SST version: {version}")
        };

        return (parser, version);
    }

    private static MemoryStream CreateReadOnlyMemoryStream(ReadOnlyMemory<byte> bytes)
    {
        if (MemoryMarshal.TryGetArray(bytes, out var segment) && segment.Array is not null)
        {
            return new MemoryStream(
                segment.Array,
                segment.Offset,
                segment.Count,
                writable: false,
                publiclyVisible: false);
        }

        return new MemoryStream(bytes.ToArray(), writable: false);
    }

    #endregion Initializers
}
