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
    public bool Malformed { get; set; }
    public string? MalformedMessage { get; set; }

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
        return FromStream(new MemoryStream(bytes));
    }

    private static (ISstParser Parser, byte Version) CreateParser(BinaryReader reader)
    {
        var magic = reader.ReadBytes(3);
        if (Encoding.ASCII.GetString(magic) != "SST")
        {
            logger.Verbose("Rejected stream because SST magic header was missing");
            throw new FormatException("Data is not SST format");
        }

        var version = reader.ReadByte();

        ISstParser parser = version switch
        {
            3 => new SstV3Parser(),
            4 => new SstV4TlvParser(),
            _ => throw new FormatException($"Unsupported SST version: {version}")
        };

        logger.Verbose("Selected SST parser version {Version}", version);

        return (parser, version);
    }

    #endregion Initializers
}
