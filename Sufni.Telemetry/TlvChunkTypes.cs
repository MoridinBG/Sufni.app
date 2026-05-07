namespace Sufni.Telemetry;

public enum TlvChunkType : byte
{
    Rates = 0x00,
    Telemetry = 0x01,
    Marker = 0x02,
    Imu = 0x03,
    ImuMeta = 0x04,
    Gps = 0x05,
    Temperature = 0x06
}
