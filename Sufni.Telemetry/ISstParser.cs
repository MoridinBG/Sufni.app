namespace Sufni.Telemetry;

public interface ISstParser
{
    SstFileInspection Inspect(BinaryReader reader, byte version);
    RawTelemetryData Parse(BinaryReader reader, byte version);
}
