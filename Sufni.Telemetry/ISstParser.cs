namespace Sufni.Telemetry;

public interface ISstParser
{
    RawTelemetryData Parse(BinaryReader reader, byte version);
}
