using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace Sufni.Telemetry;

public class RawTelemetryData
{
    private static readonly ILogger logger = Log.ForContext<RawTelemetryData>();

    #region Public properties

    public byte[] Magic { get; set; } = null!;
    public byte Version { get; set; }
    public ushort SampleRate { get; set; }
    public long Timestamp { get; set; }
    public ushort[] Front { get; set; } = [];
    public ushort[] Rear { get; set; } = [];
    public double FrontAnomalyRate { get; set; }
    public double RearAnomalyRate { get; set; }
    public MarkerData[] Markers { get; set; } = [];
    public RawImuData? ImuData { get; set; }
    public GpsRecord[]? GpsData { get; set; }
    public TemperatureSample[] TemperatureData { get; set; } = [];
    public bool Malformed { get; set; }
    public string? MalformedMessage { get; set; }
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
        logger.Verbose("Inspecting SST stream");
        using var reader = new BinaryReader(stream);
        var (parser, version) = CreateParser(reader);
        logger.Verbose("Using SST parser version {Version} for inspection", version);
        return parser.Inspect(reader, version);
    }

    public static SstFileInspection InspectByteArray(byte[] bytes)
    {
        return InspectStream(new MemoryStream(bytes));
    }

    public static RawTelemetryData FromStream(Stream stream)
    {
        logger.Verbose("Parsing SST stream");
        using var reader = new BinaryReader(stream);
        var (parser, version) = CreateParser(reader);
        logger.Verbose("Using SST parser version {Version} for parsing", version);
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
            return new SstV5Parser().Parse(bytes[4..], SstV5Constants.Version);
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
            logger.Verbose("Rejected stream because SST magic header was incomplete");
            throw new FormatException("Data is not SST format");
        }

        if (magic is [(byte)'S', (byte)'S', (byte)'T', (byte)'5'])
        {
            logger.Verbose("Selected SST parser version {Version}", SstV5Constants.Version);
            return (new SstV5Parser(), SstV5Constants.Version);
        }

        if (magic[0] != (byte)'S' || magic[1] != (byte)'S' || magic[2] != (byte)'T')
        {
            logger.Verbose("Rejected stream because SST magic header was missing");
            throw new FormatException("Data is not SST format");
        }

        var version = magic[3];

        ISstParser parser = version switch
        {
            3 => new SstV3Parser(),
            4 => new SstV4TlvParser(),
            _ => throw new FormatException($"Unsupported SST version: {version}")
        };

        logger.Verbose("Selected SST parser version {Version}", version);

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
