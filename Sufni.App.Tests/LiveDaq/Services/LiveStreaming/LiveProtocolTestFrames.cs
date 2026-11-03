using System.Buffers.Binary;
using Sufni.Telemetry;

using Sufni.App.LiveDaq.Services.LiveStreaming;
namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

internal static class LiveProtocolTestFrames
{
    public static LiveSessionHeader CreateSessionHeaderModel(
        uint sessionId = 77,
        LiveImuLocationMask imuMask = LiveImuLocationMask.Frame | LiveImuLocationMask.Rear,
        LiveSensorInstanceMask requestedSensorMask = LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.RearImu | LiveSensorInstanceMask.Gps,
        LiveSensorInstanceMask acceptedSensorMask = LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.RearImu | LiveSensorInstanceMask.Gps,
        uint acceptedTravelRateMhz = 200_000,
        uint acceptedImuRateMhz = 100_000,
        uint acceptedGpsRateMhz = 10_000,
        bool expectsFinalStatus = false)
    {
        return new LiveSessionHeader(
            SessionId: sessionId,
            AcceptedTravelRateMhz: acceptedTravelRateMhz,
            AcceptedImuRateMhz: acceptedImuRateMhz,
            AcceptedGpsRateMhz: acceptedGpsRateMhz,
            SessionStartUtc: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            SessionStartMonotonicUs: 123456789,
            ActiveImuMask: imuMask,
            ImuCalibrationScales: new LiveImuCalibrationScales(
                FrameAccelLsbPerG: 16384f,
                ForkAccelLsbPerG: 0f,
                RearAccelLsbPerG: 8192f,
                FrameGyroLsbPerDps: 131f,
                ForkGyroLsbPerDps: 0f,
                RearGyroLsbPerDps: 65.5f),
            RequestedSensorMask: requestedSensorMask,
            AcceptedSensorMask: acceptedSensorMask) { ExpectsFinalStatus = expectsFinalStatus };
    }

    public static byte[] CreateStartAckFrame(
        uint sequence,
        LiveStartErrorCode result = LiveStartErrorCode.Ok,
        uint sessionId = 77,
        LiveStreamMask selectedStreamMask = LiveStreamMask.Travel | LiveStreamMask.Imu)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), ResultCode(result));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), ((selectedStreamMask.HasFlag(LiveStreamMask.Travel) ? 1u : 0u) |
            (selectedStreamMask.HasFlag(LiveStreamMask.Imu) ? 2u : 0u) |
            (selectedStreamMask.HasFlag(LiveStreamMask.Gps) ? 4u : 0u)));
        return CreateFrame(16, sequence, payload);
    }

    public static byte[] CreateIdentifyAckFrame(uint sequence, byte[] boardSerial)
    {
        var payload = new byte[8];
        boardSerial.AsSpan(0, 8).CopyTo(payload);
        return CreateFrame(21, sequence, payload);
    }

    public static byte[] CreateErrorFrame(uint sequence, LiveStartErrorCode errorCode)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, ResultCode(errorCode));
        return CreateFrame(18, sequence, payload);
    }

    public static byte[] CreateStopAckFrame(uint sequence, uint sessionId)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, sessionId);
        return CreateFrame(17, sequence, payload);
    }

    public static byte[] CreateSessionHeaderFrame(uint sequence, LiveSessionHeader? header = null)
    {
        header ??= CreateSessionHeaderModel();

        var payload = new byte[72];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), header.SessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), header.AcceptedTravelHz);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), header.AcceptedImuHz);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), header.AcceptedGpsFixHz);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(16, 8), header.SessionStartUtc.ToUnixTimeSeconds());
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(24, 8), header.SessionStartMonotonicUs);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32, 4), ((header.ActiveImuMask.HasFlag(LiveImuLocationMask.Frame) ? 1u : 0u) |
            (header.ActiveImuMask.HasFlag(LiveImuLocationMask.Fork) ? 2u : 0u) |
            (header.ActiveImuMask.HasFlag(LiveImuLocationMask.Rear) ? 4u : 0u)));
        WriteSingleLittleEndian(payload.AsSpan(36, 4), header.ImuCalibrationScales.FrameAccelLsbPerG);
        WriteSingleLittleEndian(payload.AsSpan(40, 4), header.ImuCalibrationScales.ForkAccelLsbPerG);
        WriteSingleLittleEndian(payload.AsSpan(44, 4), header.ImuCalibrationScales.RearAccelLsbPerG);
        WriteSingleLittleEndian(payload.AsSpan(48, 4), header.ImuCalibrationScales.FrameGyroLsbPerDps);
        WriteSingleLittleEndian(payload.AsSpan(52, 4), header.ImuCalibrationScales.ForkGyroLsbPerDps);
        WriteSingleLittleEndian(payload.AsSpan(56, 4), header.ImuCalibrationScales.RearGyroLsbPerDps);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(60, 4), 0x03);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(64, 4), SensorBits(header.RequestedSensorMask));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(68, 4), SensorBits(header.AcceptedSensorMask));
        return CreateFrame(20, sequence, payload);
    }

    public static byte[] CreateTravelBatchFrame(
        uint sequence,
        uint sessionId,
        LiveTravelRecord record)
    {
        var payload = CreateBatchPayload(sessionId, 4);
        var recordOffset = 28;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(recordOffset, 2), record.ForkAngle);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(recordOffset + 2, 2), record.ShockAngle);
        return CreateFrame(32, sequence, payload);
    }

    public static byte[] CreateImuBatchFrame(
        uint sequence,
        uint sessionId,
        ImuRecord record)
    {
        var payload = CreateBatchPayload(sessionId, 12);
        var recordOffset = 28;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(recordOffset, 2), record.Ax);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(recordOffset + 2, 2), record.Ay);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(recordOffset + 4, 2), record.Az);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(recordOffset + 6, 2), record.Gx);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(recordOffset + 8, 2), record.Gy);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(recordOffset + 10, 2), record.Gz);
        return CreateFrame(33, sequence, payload);
    }

    public static byte[] CreateGpsBatchFrame(
        uint sequence,
        uint sessionId,
        GpsRecord record)
    {
        var payload = CreateBatchPayload(sessionId, 46);
        var timestamp = record.Timestamp.ToUniversalTime();
        var date = (uint)(timestamp.Year * 10000 + timestamp.Month * 100 + timestamp.Day);
        var timeOfDay = timestamp.TimeOfDay;
        var timeMs = (uint)timeOfDay.TotalMilliseconds;
        var recordOffset = 28;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(recordOffset + 0, 4), date);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(recordOffset + 4, 4), timeMs);
        WriteDoubleLittleEndian(payload.AsSpan(recordOffset + 8, 8), record.Latitude);
        WriteDoubleLittleEndian(payload.AsSpan(recordOffset + 16, 8), record.Longitude);
        WriteSingleLittleEndian(payload.AsSpan(recordOffset + 24, 4), record.Altitude);
        WriteSingleLittleEndian(payload.AsSpan(recordOffset + 28, 4), record.Speed);
        WriteSingleLittleEndian(payload.AsSpan(recordOffset + 32, 4), record.Heading);
        payload[recordOffset + 36] = record.FixMode;
        payload[recordOffset + 37] = record.Satellites;
        WriteSingleLittleEndian(payload.AsSpan(recordOffset + 38, 4), record.Epe2d);
        WriteSingleLittleEndian(payload.AsSpan(recordOffset + 42, 4), record.Epe3d);
        return CreateFrame(34, sequence, payload);
    }

    public static byte[] CreateSessionStatsFrame(uint sequence, uint sessionId)
    {
        var payload = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24, 4), 6);
        return CreateFrame(48, sequence, payload);
    }

    private static int ResultCode(LiveStartErrorCode value) => value switch
    {
        LiveStartErrorCode.Ok => 0,
        LiveStartErrorCode.InvalidRequest => -1,
        LiveStartErrorCode.Busy => -2,
        LiveStartErrorCode.NoSensorsStarted => -5,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static uint SensorBits(LiveSensorInstanceMask mask) =>
        (mask.HasFlag(LiveSensorInstanceMask.ForkTravel) ? 1u : 0u) |
        (mask.HasFlag(LiveSensorInstanceMask.ShockTravel) ? 2u : 0u) |
        (mask.HasFlag(LiveSensorInstanceMask.FrameImu) ? 4u : 0u) |
        (mask.HasFlag(LiveSensorInstanceMask.ForkImu) ? 8u : 0u) |
        (mask.HasFlag(LiveSensorInstanceMask.RearImu) ? 16u : 0u) |
        (mask.HasFlag(LiveSensorInstanceMask.Gps) ? 32u : 0u);

    // Firmware LIVE v2 envelope; do not use the production writer as its own oracle.
    private static byte[] CreateFrame(ushort type, uint sequence, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[16 + payload.Length];
        "LIVE"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), sequence);
        payload.CopyTo(bytes.AsSpan(16));
        return bytes;
    }

    private static byte[] CreateBatchPayload(uint sessionId, int recordSize)
    {
        var payload = new byte[28 + recordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 3);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8, 8), 42);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(16, 8), 555000);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24, 4), 1);
        return payload;
    }

    private static void WriteSingleLittleEndian(Span<byte> destination, float value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value));
    }

    private static void WriteDoubleLittleEndian(Span<byte> destination, double value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(destination, BitConverter.DoubleToInt64Bits(value));
    }
}
