namespace Sufni.Telemetry;

public static class SstV5ProtocolConstants
{
    public const byte Version = 5;

    public const int HeaderRemainderSize = 20;
    public const int FixedHeaderBytes = 24;
    public const int ChunkEnvelopeSize = 8;
    public const int MetadataHeaderSize = 12;
    public const int StreamDescriptorSize = 24;
    public const int GpsStreamDescriptorTailSize = 8;
    public const int SourceDescriptorSize = 16;
    public const int TravelSourceDescriptorTailSize = 8;
    public const int ImuSourceDescriptorTailSize = 12;
    public const int TemperatureSourceDescriptorTailSize = 8;
    public const int OmissionRecordSize = 8;
    public const int DataHeaderSize = 26;
    public const int FinalStatusHeaderSize = 12;
    public const int FinalStatusRecordSize = 36;
    public const int Lc76GRecordSize = 38;
    public const int M8NRecordSize = 30;
    public const int GpsDiagnosticsRecordSize = 17;

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

    public static uint StreamMaskForKind(byte streamKind) => 1u << streamKind;

    public static bool IsKnownStreamKind(byte streamKind) =>
        streamKind is >= StreamTravel and <= StreamMarker;

    public static bool IsKnownChunkType(ushort chunkType) =>
        chunkType is >= ChunkSessionMetadata and <= ChunkFinalStatus;

    public static byte StreamKindForDataChunk(ushort chunkType) => chunkType switch
    {
        ChunkTravelData => StreamTravel,
        ChunkImuData => StreamImu,
        ChunkTemperatureData => StreamTemperature,
        ChunkGpsData => StreamGps,
        ChunkBatteryData => StreamBattery,
        ChunkMarkerData => StreamMarker,
        _ => throw new InvalidOperationException("Chunk type is not a data chunk."),
    };

    public static bool IsSingleBitMask(uint mask) => mask != 0 && (mask & (mask - 1)) == 0;

    public static bool SourceBelongsToStream(byte streamKind, uint sourceBitMask) => streamKind switch
    {
        StreamTravel => (sourceBitMask & (SensorForkTravel | SensorShockTravel)) != 0,
        StreamImu => (sourceBitMask & (SensorFrameImu | SensorForkImu | SensorRearImu)) != 0,
        StreamTemperature => (sourceBitMask & (SensorFrameImu | SensorForkImu | SensorRearImu)) != 0,
        _ => false,
    };

    public static bool TryGetImuLocationId(uint sourceBitMask, out byte locationId)
    {
        locationId = sourceBitMask switch
        {
            SensorFrameImu => (byte)ImuLocation.Frame,
            SensorForkImu => (byte)ImuLocation.Fork,
            SensorRearImu => (byte)ImuLocation.Shock,
            _ => byte.MaxValue,
        };

        return locationId != byte.MaxValue;
    }

    public static byte GetImuLocationId(uint sourceBitMask)
    {
        if (TryGetImuLocationId(sourceBitMask, out var locationId))
        {
            return locationId;
        }

        throw new FormatException("SST v5 IMU location source bit is invalid.");
    }
}

internal static class SstV5Constants
{
    public const byte Version = SstV5ProtocolConstants.Version;

    public const byte StreamTravel = SstV5ProtocolConstants.StreamTravel;
    public const byte StreamImu = SstV5ProtocolConstants.StreamImu;
    public const byte StreamTemperature = SstV5ProtocolConstants.StreamTemperature;
    public const byte StreamGps = SstV5ProtocolConstants.StreamGps;
    public const byte StreamBattery = SstV5ProtocolConstants.StreamBattery;
    public const byte StreamMarker = SstV5ProtocolConstants.StreamMarker;

    public const ushort ChunkSessionMetadata = SstV5ProtocolConstants.ChunkSessionMetadata;
    public const ushort ChunkTravelData = SstV5ProtocolConstants.ChunkTravelData;
    public const ushort ChunkImuData = SstV5ProtocolConstants.ChunkImuData;
    public const ushort ChunkTemperatureData = SstV5ProtocolConstants.ChunkTemperatureData;
    public const ushort ChunkGpsData = SstV5ProtocolConstants.ChunkGpsData;
    public const ushort ChunkBatteryData = SstV5ProtocolConstants.ChunkBatteryData;
    public const ushort ChunkMarkerData = SstV5ProtocolConstants.ChunkMarkerData;
    public const ushort ChunkFinalStatus = SstV5ProtocolConstants.ChunkFinalStatus;

    public const byte TimingFixedRate = SstV5ProtocolConstants.TimingFixedRate;
    public const byte TimingMonotonicEventStatus = SstV5ProtocolConstants.TimingMonotonicEventStatus;
    public const byte TimingGpsReceiverTimed = SstV5ProtocolConstants.TimingGpsReceiverTimed;

    public const uint SensorForkTravel = SstV5ProtocolConstants.SensorForkTravel;
    public const uint SensorShockTravel = SstV5ProtocolConstants.SensorShockTravel;
    public const uint SensorFrameImu = SstV5ProtocolConstants.SensorFrameImu;
    public const uint SensorForkImu = SstV5ProtocolConstants.SensorForkImu;
    public const uint SensorRearImu = SstV5ProtocolConstants.SensorRearImu;
    public const uint SensorGps = SstV5ProtocolConstants.SensorGps;
    public const uint SensorBattery = SstV5ProtocolConstants.SensorBattery;

    public const byte EncodingTravelAdjustedU16 = SstV5ProtocolConstants.EncodingTravelAdjustedU16;
    public const byte EncodingImuI16X6CalCounts = SstV5ProtocolConstants.EncodingImuI16X6CalCounts;
    public const byte EncodingTemperatureRawI16 = SstV5ProtocolConstants.EncodingTemperatureRawI16;
    public const byte EncodingGpsNavFixV1 = SstV5ProtocolConstants.EncodingGpsNavFixV1;
    public const byte EncodingGpsDiagPublicV1 = SstV5ProtocolConstants.EncodingGpsDiagPublicV1;
    public const byte EncodingBatteryAdcMvU16Flags = SstV5ProtocolConstants.EncodingBatteryAdcMvU16Flags;
    public const byte EncodingMarkerEventTypeU8 = SstV5ProtocolConstants.EncodingMarkerEventTypeU8;

    public const byte GpsDriverLc76G = SstV5ProtocolConstants.GpsDriverLc76G;
    public const byte GpsDriverM8N = SstV5ProtocolConstants.GpsDriverM8N;

    public const byte MarkerManualUserMark = SstV5ProtocolConstants.MarkerManualUserMark;

    public const uint ExtensionGpsDiagPublicV1 = SstV5ProtocolConstants.ExtensionGpsDiagPublicV1;
}
