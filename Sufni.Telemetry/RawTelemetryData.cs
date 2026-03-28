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
    
    public static RawTelemetryData FromStream(Stream stream)
    {
        using var reader = new BinaryReader(stream);

        var magic = reader.ReadBytes(3);
        if (Encoding.ASCII.GetString(magic) != "SST")
            throw new Exception("Data is not SST format");

        var version = reader.ReadByte();

        ISstParser parser = version switch
        {
            3 => new SstV3Parser(),
            4 => new SstV4TlvParser(),
            _ => throw new Exception($"Unsupported SST version: {version}")
        };

        return parser.Parse(reader, version);
    }

    public static RawTelemetryData FromByteArray(byte[] bytes)
    {
        return FromStream(new MemoryStream(bytes));
    }
    
    #endregion Initializers
}
