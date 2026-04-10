using System.Text;

namespace Sufni.Telemetry;

public class RawTelemetryData
{
    #region Public properties

    public byte[] Magic { get; set; } = null!;
    public byte Version { get; set; }
    public ushort SampleRate { get; set; }
    public int Timestamp { get; set; }
    public ushort[] Front { get; set; } = [];
    public ushort[] Rear { get; set; } = [];
    public double FrontAnomalyRate { get; set; }
    public double RearAnomalyRate { get; set; }
    public MarkerData[] Markers { get; set; } = [];
    public RawImuData? ImuData { get; set; }
    public GpsRecord[]? GpsData { get; set; }
    public bool Malformed { get; set; }

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
        return FromStream(new MemoryStream(bytes));
    }

    private static (ISstParser Parser, byte Version) CreateParser(BinaryReader reader)
    {
        var magic = reader.ReadBytes(3);
        if (Encoding.ASCII.GetString(magic) != "SST")
            throw new FormatException("Data is not SST format");

        var version = reader.ReadByte();

        ISstParser parser = version switch
        {
            3 => new SstV3Parser(),
            4 => new SstV4TlvParser(),
            _ => throw new FormatException($"Unsupported SST version: {version}")
        };

        return (parser, version);
    }

    #endregion Initializers
}
