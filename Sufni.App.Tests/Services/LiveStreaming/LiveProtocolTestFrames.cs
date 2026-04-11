using System.Buffers.Binary;
using Sufni.App.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services.LiveStreaming;

internal static class LiveProtocolTestFrames
{
    public static LiveSessionHeader CreateSessionHeaderModel(
        uint sessionId = 77,
        LiveSensorMask sensorMask = LiveSensorMask.Travel | LiveSensorMask.Imu | LiveSensorMask.Gps,
        LiveImuLocationMask imuMask = LiveImuLocationMask.Frame | LiveImuLocationMask.Rear)
    {
        return new LiveSessionHeader(
            SessionId: sessionId,
            SelectedSensorMask: sensorMask,
            PublishCadenceMs: 20,
            AcceptedTravelHz: 200,
            AcceptedImuHz: 100,
            AcceptedGpsFixHz: 10,
            TravelPeriodUs: 5000,
            ImuPeriodUs: 10000,
            GpsFixIntervalMs: 100,
            SessionStartUtc: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            SessionStartMonotonicUs: 123456789,
            ActiveImuCount: 2,
            ActiveImuMask: imuMask,
            ImuCalibrationScales: new LiveImuCalibrationScales(
                FrameAccelLsbPerG: 16384f,
                ForkAccelLsbPerG: 0f,
                RearAccelLsbPerG: 8192f,
                FrameGyroLsbPerDps: 131f,
                ForkGyroLsbPerDps: 0f,
                RearGyroLsbPerDps: 65.5f),
            TravelQueueCapacity: 6,
            ImuQueueCapacity: 6,
            GpsQueueCapacity: 4,
            Flags: LiveSessionFlags.CalibratedOnly | LiveSessionFlags.MutuallyExclusiveWithRecording);
    }

    public static byte[] CreateStartAckFrame(
        uint sequence,
        LiveStartErrorCode result = LiveStartErrorCode.Ok,
        uint sessionId = 77,
        LiveSensorMask selectedSensorMask = LiveSensorMask.Travel | LiveSensorMask.Imu)
    {
        var payload = new byte[LiveProtocolConstants.StartAckPayloadSize];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), (int)result);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), (uint)selectedSensorMask);
        return LiveProtocolReader.CreateFrame(LiveFrameType.StartLiveAck, sequence, payload);
    }

    public static byte[] CreateErrorFrame(uint sequence, LiveStartErrorCode errorCode)
    {
        var payload = new byte[LiveProtocolConstants.ErrorPayloadSize];
        BinaryPrimitives.WriteInt32LittleEndian(payload, (int)errorCode);
        return LiveProtocolReader.CreateFrame(LiveFrameType.Error, sequence, payload);
    }

    public static byte[] CreateStopAckFrame(uint sequence, uint sessionId)
    {
        var payload = new byte[LiveProtocolConstants.StopAckPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, sessionId);
        return LiveProtocolReader.CreateFrame(LiveFrameType.StopLiveAck, sequence, payload);
    }

    public static byte[] CreateSessionHeaderFrame(uint sequence, LiveSessionHeader? header = null)
    {
        header ??= CreateSessionHeaderModel();

        var payload = new byte[LiveProtocolConstants.SessionHeaderPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), header.SessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), (uint)header.SelectedSensorMask);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), header.PublishCadenceMs);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), header.AcceptedTravelHz);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), header.AcceptedImuHz);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), header.AcceptedGpsFixHz);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24, 4), header.TravelPeriodUs);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28, 4), header.ImuPeriodUs);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32, 4), header.GpsFixIntervalMs);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(36, 8), header.SessionStartUtc.ToUnixTimeSeconds());
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(44, 8), header.SessionStartMonotonicUs);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(52, 4), header.ActiveImuCount);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(56, 4), (uint)header.ActiveImuMask);
        WriteSingleLittleEndian(payload.AsSpan(60, 4), header.ImuCalibrationScales.FrameAccelLsbPerG);
        WriteSingleLittleEndian(payload.AsSpan(64, 4), header.ImuCalibrationScales.ForkAccelLsbPerG);
        WriteSingleLittleEndian(payload.AsSpan(68, 4), header.ImuCalibrationScales.RearAccelLsbPerG);
        WriteSingleLittleEndian(payload.AsSpan(72, 4), header.ImuCalibrationScales.FrameGyroLsbPerDps);
        WriteSingleLittleEndian(payload.AsSpan(76, 4), header.ImuCalibrationScales.ForkGyroLsbPerDps);
        WriteSingleLittleEndian(payload.AsSpan(80, 4), header.ImuCalibrationScales.RearGyroLsbPerDps);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(84, 4), header.TravelQueueCapacity);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(88, 4), header.ImuQueueCapacity);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(92, 4), header.GpsQueueCapacity);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(96, 4), (uint)header.Flags);
        return LiveProtocolReader.CreateFrame(LiveFrameType.SessionHeader, sequence, payload);
    }

    public static byte[] CreateGpsBatchFrame(
        uint sequence,
        uint sessionId,
        GpsRecord record,
        uint queueDepth = 1,
        uint droppedBatches = 0)
    {
        var payload = new byte[LiveProtocolConstants.BatchHeaderSize + LiveProtocolConstants.GpsRecordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), (uint)LiveStreamType.Gps);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 3);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(12, 8), 42);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(20, 8), 555000);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32, 4), LiveProtocolConstants.GpsRecordSize);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36, 4), queueDepth);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(40, 4), droppedBatches);

        var timestamp = record.Timestamp.ToUniversalTime();
        var date = (uint)(timestamp.Year * 10000 + timestamp.Month * 100 + timestamp.Day);
        var timeOfDay = timestamp.TimeOfDay;
        var timeMs = (uint)timeOfDay.TotalMilliseconds;
        var recordOffset = LiveProtocolConstants.BatchHeaderSize;
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
        return LiveProtocolReader.CreateFrame(LiveFrameType.GpsBatch, sequence, payload);
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