namespace Sufni.Telemetry;

internal static class SstV5Constants
{
    public const byte Version = 5;

    public const byte StreamTravel = 1;
    public const byte StreamImu = 2;
    public const byte StreamTemperature = 3;
    public const byte StreamGps = 4;
    public const byte StreamBattery = 5;
    public const byte StreamMarker = 6;

    public const ushort ChunkSessionMetadata = 1;
    public const ushort ChunkTravelData = 2;
    public const ushort ChunkImuData = 3;
    public const ushort ChunkTemperatureData = 4;
    public const ushort ChunkGpsData = 5;
    public const ushort ChunkBatteryData = 6;
    public const ushort ChunkMarkerData = 7;
    public const ushort ChunkFinalStatus = 8;

    public const byte TimingFixedRate = 1;
    public const byte TimingMonotonicEventStatus = 2;
    public const byte TimingGpsReceiverTimed = 3;

    public const uint SensorForkTravel = 0x00000001;
    public const uint SensorShockTravel = 0x00000002;
    public const uint SensorFrameImu = 0x00000004;
    public const uint SensorForkImu = 0x00000008;
    public const uint SensorRearImu = 0x00000010;
    public const uint SensorGps = 0x00000020;
    public const uint SensorBattery = 0x00000040;

    public const byte EncodingTravelAdjustedU16 = 1;
    public const byte EncodingImuI16X6CalCounts = 2;
    public const byte EncodingTemperatureRawI16 = 3;
    public const byte EncodingGpsNavFixV1 = 4;
    public const byte EncodingGpsDiagPublicV1 = 5;
    public const byte EncodingBatteryAdcMvU16Flags = 6;
    public const byte EncodingMarkerEventTypeU8 = 7;

    public const byte GpsDriverLc76G = 1;
    public const byte GpsDriverM8N = 2;

    public const byte MarkerManualUserMark = 1;

    public const uint ExtensionGpsDiagPublicV1 = 0x00000001;
}
