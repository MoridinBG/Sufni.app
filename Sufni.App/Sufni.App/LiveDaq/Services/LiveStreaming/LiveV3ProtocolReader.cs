using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sufni.App.Acquisition.Services;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed class LiveV3ProtocolReader
{
    private readonly FramedMessageReader frameReader = new(
        LiveV3ProtocolConstants.FrameHeaderSize,
        static headerBytes => ParseHeader(headerBytes).TotalFrameLength);
    private LiveV3SessionDecodeContext context = new();

    public int BufferedByteCount => frameReader.BufferedByteCount;
    public LiveV3SessionDecodeContext Context => context;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        frameReader.Append(bytes);
    }

    public void Reset()
    {
        frameReader.Reset();
        ResetSessionContext();
    }

    public void ResetSessionContext()
    {
        context = new LiveV3SessionDecodeContext();
    }

    public bool TryReadFrame(out LiveProtocolFrame? frame)
    {
        return frameReader.TryReadFrame(frameBytes => ParseFrame(frameBytes, context), out frame);
    }

    public static byte[] CreateHandshake()
    {
        var handshake = new byte[LiveV3ProtocolConstants.HandshakeSize];
        Encoding.ASCII.GetBytes(LiveV3ProtocolConstants.HandshakeMagic, handshake);
        return handshake;
    }

    public static byte[] CreateServerHello(
        ulong uniqueBoardId,
        byte protoMinor = 0,
        ushort featureFlags = 0,
        byte firmwareVersionMajor = 0,
        byte firmwareVersionMinor = 0,
        byte firmwareVersionPatch = 0)
    {
        var hello = new byte[LiveV3ProtocolConstants.ServerHelloSize];
        Encoding.ASCII.GetBytes(LiveV3ProtocolConstants.HandshakeMagic, hello);
        hello[4] = LiveV3ProtocolConstants.ProtocolMajor;
        hello[5] = protoMinor;
        BinaryPrimitives.WriteUInt16LittleEndian(hello.AsSpan(6, 2), featureFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(8, 4), LiveV3ProtocolConstants.MaxPayloadLength);
        BinaryPrimitives.WriteUInt64LittleEndian(hello.AsSpan(12, 8), uniqueBoardId);
        hello[20] = firmwareVersionMajor;
        hello[21] = firmwareVersionMinor;
        hello[22] = firmwareVersionPatch;
        return hello;
    }

    public static LiveV3ServerHello ParseServerHello(ReadOnlySpan<byte> helloBytes)
    {
        if (helloBytes.Length < LiveV3ProtocolConstants.ServerHelloSize)
        {
            throw new FormatException("LIVE v3 server hello is truncated.");
        }

        if (helloBytes.Length != LiveV3ProtocolConstants.ServerHelloSize)
        {
            throw new FormatException("LIVE v3 server hello length is invalid.");
        }

        if (!helloBytes[..4].SequenceEqual(Encoding.ASCII.GetBytes(LiveV3ProtocolConstants.HandshakeMagic)))
        {
            throw new FormatException("LIVE v3 server hello magic is invalid.");
        }

        if (helloBytes[4] != LiveV3ProtocolConstants.ProtocolMajor)
        {
            throw new FormatException("LIVE v3 server hello protocol major is unsupported.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(helloBytes[8..12]) != LiveV3ProtocolConstants.MaxPayloadLength)
        {
            throw new FormatException("LIVE v3 server hello max payload length is unsupported.");
        }

        var uniqueBoardId = BinaryPrimitives.ReadUInt64LittleEndian(helloBytes[12..20]);
        if (uniqueBoardId == 0)
        {
            throw new FormatException("LIVE v3 server hello board ID is invalid.");
        }

        if (helloBytes[23] != 0)
        {
            throw new FormatException("LIVE v3 server hello reserved field is invalid.");
        }

        return new LiveV3ServerHello(
            ProtoMinor: helloBytes[5],
            FeatureFlags: BinaryPrimitives.ReadUInt16LittleEndian(helloBytes[6..8]),
            MaxFramePayloadBytes: BinaryPrimitives.ReadUInt32LittleEndian(helloBytes[8..12]),
            UniqueBoardId: uniqueBoardId,
            FirmwareVersion: new Version(helloBytes[20], helloBytes[21], helloBytes[22]));
    }

    public static byte[] CreateCapabilitiesRequestFrame(uint sequence) =>
        CreateFrame(LiveV3FrameType.CapabilitiesReq, sessionId: 0, sequence, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateStartRequestFrame(uint sequence, LiveV3StartRequest request)
    {
        var streamRequests = request.StreamRequests;
        if (streamRequests.Count is < 1 or > LiveV3ProtocolConstants.MaxStartRequestRecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "LIVE v3 START_REQ must contain between 1 and 6 stream records.");
        }

        if (GetStartRequestValidationError(request) is { } validationError)
        {
            throw new ArgumentException(validationError, nameof(request));
        }

        var payload = new byte[
            LiveV3ProtocolConstants.StartRequestHeaderSize +
            streamRequests.Count * LiveV3ProtocolConstants.StreamRequestRecordSize];
        var offset = 0;
        payload[offset++] = checked((byte)streamRequests.Count);
        payload[offset++] = 0;
        WriteUInt16(payload, ref offset, request.StartFlags);

        foreach (var record in streamRequests)
        {
            payload[offset++] = record.StreamKind;
            payload[offset++] = 0;
            WriteUInt16(payload, ref offset, record.RecordFlags);
            WriteUInt32(payload, ref offset, (uint)record.SourceMask);
            WriteUInt32(payload, ref offset, record.ExtensionMask);
            WriteUInt32(payload, ref offset, record.RateMhz);
            WriteUInt32(payload, ref offset, record.BatchDurationMs);
        }

        return CreateFrame(LiveV3FrameType.StartReq, sessionId: 0, sequence, payload);
    }

    public static byte[] CreateStopRequestFrame(byte sessionId, uint sequence) =>
        CreateFrame(LiveV3FrameType.StopReq, sessionId, sequence, ReadOnlySpan<byte>.Empty);

    public static byte[] CreatePingFrame(byte sessionId, uint sequence, uint nonce)
    {
        var payload = new byte[LiveV3ProtocolConstants.PingPongPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, nonce);
        return CreateFrame(LiveV3FrameType.Ping, sessionId, sequence, payload);
    }

    public static byte[] CreateDeviceStateRequestFrame(uint sequence) =>
        CreateFrame(LiveV3FrameType.DeviceStateReq, sessionId: 0, sequence, ReadOnlySpan<byte>.Empty);

    public static LiveV3FrameHeader ParseHeader(ReadOnlySpan<byte> headerBytes, bool allowUnknownFrameType = false)
    {
        if (headerBytes.Length < LiveV3ProtocolConstants.FrameHeaderSize)
        {
            throw new FormatException("LIVE v3 frame header is truncated.");
        }

        var rawFrameType = headerBytes[1];
        var frameType = (LiveV3FrameType)rawFrameType;
        if (!allowUnknownFrameType && !IsKnownFrameType(frameType))
        {
            throw new FormatException($"LIVE v3 frame type {rawFrameType} is invalid.");
        }

        var header = new LiveV3FrameHeader(
            SessionId: headerBytes[0],
            FrameType: frameType,
            FrameFlags: BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[2..4]),
            PayloadLength: BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[4..8]),
            TxSequence: BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[8..12]));

        if (header.FrameFlags != 0)
        {
            throw new FormatException("LIVE v3 frame flags are invalid.");
        }

        if (header.PayloadLength > LiveV3ProtocolConstants.MaxPayloadLength)
        {
            throw new FormatException(
                $"LIVE v3 frame payload length {header.PayloadLength} exceeds maximum {LiveV3ProtocolConstants.MaxPayloadLength}.");
        }

        return header;
    }

    public static LiveProtocolFrame ParseFrame(ReadOnlySpan<byte> frameBytes, LiveV3SessionDecodeContext context)
    {
        var header = ParseHeader(frameBytes[..LiveV3ProtocolConstants.FrameHeaderSize]);
        if (frameBytes.Length != header.TotalFrameLength)
        {
            throw new FormatException("LIVE v3 frame length does not match payload length.");
        }

        var payload = frameBytes[LiveV3ProtocolConstants.FrameHeaderSize..];
        var metadata = new LiveFrameMetadata(header.TxSequence);
        return header.FrameType switch
        {
            LiveV3FrameType.CapabilitiesReq => ParseSessionZeroEmptyPayloadFrame(header, payload, new LiveV3CapabilitiesRequestFrame(metadata)),
            LiveV3FrameType.CapabilitiesResp => ParseCapabilitiesFrame(header, metadata, payload),
            LiveV3FrameType.StartReq => ParseStartRequestFrame(header, metadata, payload),
            LiveV3FrameType.StartResult => ParseStartResultFrame(header, metadata, payload, context),
            LiveV3FrameType.StopReq => ParseStopRequestFrame(header, metadata, payload, context),
            LiveV3FrameType.StopResult => new LiveStopResultFrame(metadata, ParseStopResult(header, payload, context)),
            LiveV3FrameType.SessionHeader => ParseSessionHeaderFrame(header, metadata, payload, context),
            LiveV3FrameType.SessionResult => ParseSessionResultFrame(header, metadata, payload, context),
            LiveV3FrameType.TravelData => ParseTravelDataFrame(header, metadata, payload, context),
            LiveV3FrameType.ImuData => ParseImuDataFrame(header, metadata, payload, context),
            LiveV3FrameType.TemperatureData => ParseTemperatureDataFrame(header, metadata, payload, context),
            LiveV3FrameType.GpsData => ParseGpsDataFrame(header, metadata, payload, context),
            LiveV3FrameType.BatteryData => ParseBatteryDataFrame(header, metadata, payload, context),
            LiveV3FrameType.MarkerData => ParseMarkerDataFrame(header, metadata, payload, context),
            LiveV3FrameType.Status => new LiveStatusFrame(metadata, ParseStatus(header, payload, context)),
            LiveV3FrameType.Ping => new LivePingFrame(metadata, ParseNonceFrame(header, payload, context))
            {
                SessionId = header.SessionId,
            },
            LiveV3FrameType.Pong => new LivePongFrame(metadata, ParseNonceFrame(header, payload, context))
            {
                SessionId = header.SessionId,
            },
            LiveV3FrameType.Error => ParseErrorFrame(header, metadata, payload, context),
            LiveV3FrameType.DeviceStateReq => ParseSessionZeroEmptyPayloadFrame(header, payload, new LiveV3DeviceStateRequestFrame(metadata)),
            LiveV3FrameType.DeviceStateResp => ParseDeviceStateFrame(header, metadata, payload),
            _ => throw new FormatException($"Unsupported LIVE v3 frame type {(byte)header.FrameType}."),
        };
    }

    public static byte[] CreateFrame(
        LiveV3FrameType frameType,
        byte sessionId,
        uint sequence,
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length > LiveV3ProtocolConstants.MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "LIVE v3 frame payload exceeds the maximum payload length.");
        }

        var frame = new byte[LiveV3ProtocolConstants.FrameHeaderSize + payload.Length];
        frame[0] = sessionId;
        frame[1] = (byte)frameType;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8, 4), sequence);
        payload.CopyTo(frame.AsSpan(LiveV3ProtocolConstants.FrameHeaderSize));
        return frame;
    }

    private static bool IsKnownFrameType(LiveV3FrameType frameType) => frameType switch
    {
        LiveV3FrameType.CapabilitiesReq => true,
        LiveV3FrameType.CapabilitiesResp => true,
        LiveV3FrameType.StartReq => true,
        LiveV3FrameType.StartResult => true,
        LiveV3FrameType.StopReq => true,
        LiveV3FrameType.StopResult => true,
        LiveV3FrameType.SessionHeader => true,
        LiveV3FrameType.SessionResult => true,
        LiveV3FrameType.TravelData => true,
        LiveV3FrameType.ImuData => true,
        LiveV3FrameType.TemperatureData => true,
        LiveV3FrameType.GpsData => true,
        LiveV3FrameType.BatteryData => true,
        LiveV3FrameType.MarkerData => true,
        LiveV3FrameType.Status => true,
        LiveV3FrameType.Ping => true,
        LiveV3FrameType.Pong => true,
        LiveV3FrameType.Error => true,
        LiveV3FrameType.DeviceStateReq => true,
        LiveV3FrameType.DeviceStateResp => true,
        _ => false,
    };

    private static LiveV3StartRequest ParseStartRequest(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < LiveV3ProtocolConstants.StartRequestHeaderSize)
        {
            throw new FormatException("LIVE v3 START_REQ payload is truncated.");
        }

        var offset = 0;
        var streamRequestCount = payload[offset++];
        if (streamRequestCount is < 1 or > LiveV3ProtocolConstants.MaxStartRequestRecordCount)
        {
            throw new FormatException("LIVE v3 START_REQ stream request count is invalid.");
        }

        if (payload[offset++] != 0)
        {
            throw new FormatException("LIVE v3 START_REQ reserved field is invalid.");
        }

        var expectedLength = LiveV3ProtocolConstants.StartRequestHeaderSize +
            streamRequestCount * LiveV3ProtocolConstants.StreamRequestRecordSize;
        if (payload.Length != expectedLength)
        {
            throw new FormatException("LIVE v3 START_REQ payload length is invalid.");
        }

        var startFlags = ReadUInt16(payload, ref offset);
        var records = new LiveV3StreamRequestRecord[streamRequestCount];
        for (var index = 0; index < records.Length; index++)
        {
            var streamKind = payload[offset++];
            if (payload[offset++] != 0)
            {
                throw new FormatException("LIVE v3 START_REQ stream request reserved field is invalid.");
            }

            records[index] = new LiveV3StreamRequestRecord(
                StreamKind: streamKind,
                RecordFlags: ReadUInt16(payload, ref offset),
                SourceMask: (LiveSensorInstanceMask)ReadUInt32(payload, ref offset),
                ExtensionMask: ReadUInt32(payload, ref offset),
                RateMhz: ReadUInt32(payload, ref offset),
                BatchDurationMs: ReadUInt32(payload, ref offset));
        }

        var request = new LiveV3StartRequest
        {
            StartFlags = startFlags,
            StreamRequests = records,
        };
        if (GetStartRequestValidationError(request) is { } validationError)
        {
            throw new FormatException(validationError);
        }

        return request;
    }

    private static LiveV3CapabilitiesFrame ParseCapabilitiesFrame(
        LiveV3FrameHeader header,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload)
    {
        EnsureSessionIdZero(header);
        return new LiveV3CapabilitiesFrame(metadata, ParseCapabilities(payload));
    }

    private static LiveV3StartRequestFrame ParseStartRequestFrame(
        LiveV3FrameHeader header,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload)
    {
        EnsureSessionIdZero(header);
        return new LiveV3StartRequestFrame(metadata, ParseStartRequest(payload));
    }

    private static LiveStopRequestFrame ParseStopRequestFrame(
        LiveV3FrameHeader header,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        context.ValidateSessionId(header.SessionId);
        return (LiveStopRequestFrame)ParseEmptyPayloadFrame(
            header,
            payload,
            new LiveStopRequestFrame(metadata));
    }

    private static string? GetStartRequestValidationError(LiveV3StartRequest request)
    {
        const ushort validStartFlags =
            LiveV3ProtocolConstants.StartFlagPriority |
            LiveV3ProtocolConstants.StartFlagNoGpsHeaderWait;
        const ushort validRecordFlags =
            LiveV3ProtocolConstants.StreamRequestFlagRateOverride |
            LiveV3ProtocolConstants.StreamRequestFlagBatchDurationOverride;
        if ((request.StartFlags & ~validStartFlags) != 0)
        {
            return "LIVE v3 START_REQ start flags are invalid.";
        }

        byte previousStreamKind = 0;
        var hasGps = false;
        foreach (var record in request.StreamRequests)
        {
            if (!SstV5ProtocolConstants.IsKnownStreamKind(record.StreamKind) ||
                record.StreamKind <= previousStreamKind ||
                (record.RecordFlags & ~validRecordFlags) != 0 ||
                !IsKnownSourceMask(record.SourceMask))
            {
                return "LIVE v3 START_REQ stream records must be valid and strictly ascending.";
            }

            var hasRateOverride =
                (record.RecordFlags & LiveV3ProtocolConstants.StreamRequestFlagRateOverride) != 0;
            var hasDurationOverride =
                (record.RecordFlags & LiveV3ProtocolConstants.StreamRequestFlagBatchDurationOverride) != 0;
            if (hasRateOverride != (record.RateMhz != 0) ||
                hasDurationOverride != (record.BatchDurationMs != 0))
            {
                return "LIVE v3 START_REQ override flags and values do not match.";
            }

            var streamShapeValid = record.StreamKind switch
            {
                SstV5ProtocolConstants.StreamTravel =>
                    (record.SourceMask & ~LiveSensorInstanceMask.Travel) == 0 &&
                    record.ExtensionMask == 0,
                SstV5ProtocolConstants.StreamImu =>
                    (record.SourceMask & ~LiveSensorInstanceMask.Imu) == 0 &&
                    record.ExtensionMask == 0,
                SstV5ProtocolConstants.StreamTemperature =>
                    (record.SourceMask & ~LiveSensorInstanceMask.Imu) == 0 &&
                    record.ExtensionMask == 0 &&
                    !hasDurationOverride,
                SstV5ProtocolConstants.StreamGps =>
                    record.SourceMask == LiveSensorInstanceMask.Gps &&
                    (record.ExtensionMask & ~SstV5ProtocolConstants.ExtensionGpsDiagPublicV1) == 0 &&
                    !hasDurationOverride,
                SstV5ProtocolConstants.StreamBattery =>
                    record.SourceMask == LiveSensorInstanceMask.Battery &&
                    record.ExtensionMask == 0 &&
                    !hasRateOverride &&
                    !hasDurationOverride,
                SstV5ProtocolConstants.StreamMarker =>
                    record.SourceMask == LiveSensorInstanceMask.None &&
                    record.ExtensionMask == 0 &&
                    !hasRateOverride &&
                    !hasDurationOverride,
                _ => false,
            };
            if (!streamShapeValid)
            {
                return $"LIVE v3 START_REQ stream {record.StreamKind} has invalid fields.";
            }

            hasGps |= record.StreamKind == SstV5ProtocolConstants.StreamGps;
            previousStreamKind = record.StreamKind;
        }

        if ((request.StartFlags & LiveV3ProtocolConstants.StartFlagNoGpsHeaderWait) != 0 && !hasGps)
        {
            return "LIVE v3 START_REQ NO_GPS_HEADER_WAIT requires a GPS record.";
        }
        return null;
    }

    private static LiveV3Capabilities ParseCapabilities(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < LiveV3ProtocolConstants.CapabilitiesHeaderSize)
        {
            throw new FormatException("LIVE v3 capabilities payload is truncated.");
        }

        var boardId = payload[0];
        var streamCount = payload[1];
        var capabilityFlags = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]);
        var supportedStreamMask = (LiveStreamMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
        var supportedSourceMask = (LiveSensorInstanceMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]);
        var maxFramePayloadBytes = BinaryPrimitives.ReadUInt32LittleEndian(payload[12..16]);
        if (boardId is not 1 and not 2 ||
            capabilityFlags != 0 ||
            streamCount > 6 ||
            !IsKnownStreamMask(supportedStreamMask) ||
            !IsKnownSourceMask(supportedSourceMask))
        {
            throw new FormatException("LIVE v3 capabilities header is invalid.");
        }

        if (maxFramePayloadBytes != LiveV3ProtocolConstants.MaxPayloadLength)
        {
            throw new FormatException("LIVE v3 capabilities max payload length is unsupported.");
        }

        var expectedLength = LiveV3ProtocolConstants.CapabilitiesHeaderSize +
            streamCount * LiveV3ProtocolConstants.StreamCapabilityRecordSize;
        if (payload.Length != expectedLength)
        {
            throw new FormatException("LIVE v3 capabilities payload length is invalid.");
        }

        var streams = new LiveV3StreamCapability[streamCount];
        var offset = LiveV3ProtocolConstants.CapabilitiesHeaderSize;
        byte previousStreamKind = 0;
        var describedStreamMask = LiveStreamMask.None;
        for (var index = 0; index < streams.Length; index++)
        {
            var streamKind = payload[offset];
            var timingModelId = payload[offset + 1];
            var sourceMask = (LiveSensorInstanceMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 4)..(offset + 8)]);
            var extensionMask = BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 8)..(offset + 12)]);
            var minRateMhz = BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 12)..(offset + 16)]);
            var maxRateMhz = BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 16)..(offset + 20)]);
            var maxBatchDurationMs = BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 20)..(offset + 24)]);
            if (!SstV5ProtocolConstants.IsKnownStreamKind(streamKind) ||
                streamKind <= previousStreamKind ||
                BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + 2)..(offset + 4)]) != 0 ||
                timingModelId != ExpectedTimingModel(streamKind) ||
                !IsCapabilitySourceMaskValid(streamKind, sourceMask, supportedSourceMask) ||
                !IsCapabilityExtensionMaskValid(streamKind, extensionMask) ||
                minRateMhz > maxRateMhz ||
                (streamKind is not SstV5ProtocolConstants.StreamTravel and
                    not SstV5ProtocolConstants.StreamImu && maxBatchDurationMs != 0))
            {
                throw new FormatException("LIVE v3 stream capability record is invalid.");
            }

            var streamMask = ToLiveStreamMask(streamKind);
            streams[index] = new LiveV3StreamCapability(
                Stream: streamMask,
                TimingModelId: timingModelId,
                SupportedSourceMask: sourceMask,
                SupportedExtensionMask: extensionMask,
                MinRateMhz: minRateMhz,
                MaxRateMhz: maxRateMhz,
                MaxBatchDurationMs: maxBatchDurationMs);
            describedStreamMask |= streamMask;
            previousStreamKind = streamKind;
            offset += LiveV3ProtocolConstants.StreamCapabilityRecordSize;
        }

        if (describedStreamMask != supportedStreamMask)
        {
            throw new FormatException("LIVE v3 capability stream records do not match the supported stream mask.");
        }

        return new LiveV3Capabilities(
            boardId,
            supportedStreamMask,
            supportedSourceMask,
            maxFramePayloadBytes,
            streams);
    }

    private static LiveV3StartResultFrame ParseStartResultFrame(
        LiveV3FrameHeader header,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        if (payload.Length < LiveV3ProtocolConstants.StartResultHeaderSize)
        {
            throw new FormatException("LIVE v3 START_RESULT payload is truncated.");
        }

        var resultCode = payload[0];
        var admissionReasonCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]);
        if (payload[1] != 0)
        {
            throw new FormatException("LIVE v3 START_RESULT reserved field is invalid.");
        }

        if (resultCode is not 0 and not 1)
        {
            throw new FormatException("LIVE v3 START_RESULT code is invalid.");
        }

        if ((resultCode == 0 && (header.SessionId == 0 || admissionReasonCount != 0)) ||
            (resultCode == 1 && header.SessionId != 0))
        {
            throw new FormatException("LIVE v3 START_RESULT session state is invalid.");
        }

        var expectedLength = LiveV3ProtocolConstants.StartResultHeaderSize +
            admissionReasonCount * LiveV3ProtocolConstants.AdmissionReasonRecordSize;
        if (payload.Length != expectedLength)
        {
            throw new FormatException("LIVE v3 START_RESULT payload length is invalid.");
        }

        var reasons = new LiveStartAdmissionReason[admissionReasonCount];
        var offset = LiveV3ProtocolConstants.StartResultHeaderSize;
        for (var index = 0; index < reasons.Length; index++)
        {
            var targetKind = payload[offset];
            var streamKind = payload[offset + 1];
            var reason = payload[offset + 2];
            var reserved = payload[offset + 3];
            var targetMask = BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 4)..(offset + 8)]);

            var isWholeRequestReason = targetKind == 1 && streamKind == 0 && targetMask != 0;
            var isWholeStreamReason = targetKind == 1 &&
                                      SstV5ProtocolConstants.IsKnownStreamKind(streamKind) &&
                                      targetMask == 0;
            var isSourceOrExtensionReason = targetKind is 2 or 3 &&
                                            SstV5ProtocolConstants.IsKnownStreamKind(streamKind) &&
                                            targetMask != 0;
            if (reason is < LiveV3ProtocolHelpers.AdmissionUnsupported or > LiveV3ProtocolHelpers.AdmissionNoTelemetry ||
                reserved != 0 ||
                (!isWholeRequestReason && !isWholeStreamReason && !isSourceOrExtensionReason))
            {
                throw new FormatException("LIVE v3 START_RESULT admission reason is invalid.");
            }

            reasons[index] = new LiveStartAdmissionReason(
                reason,
                targetKind == 1 && streamKind == 0
                    ? (LiveStreamMask)targetMask
                    : streamKind == 0 ? LiveStreamMask.None : ToLiveStreamMask(streamKind),
                targetKind == 2 ? (LiveSensorInstanceMask)targetMask : LiveSensorInstanceMask.None,
                targetKind,
                targetMask,
                streamKind);
            offset += LiveV3ProtocolConstants.AdmissionReasonRecordSize;
        }

        if (resultCode == 0)
        {
            context.AcceptSession(header.SessionId);
        }

        return new LiveV3StartResultFrame(
            metadata,
            new LiveV3StartResult(resultCode, header.SessionId, reasons));
    }

    private static LiveStopResult ParseStopResult(
        LiveV3FrameHeader header,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        EnsureSessionFrame(header, context);
        EnsurePayloadLength(payload, LiveV3ProtocolConstants.StopResultPayloadSize, LiveV3FrameType.StopResult);
        if (payload[0] != 0 || payload[1] != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]) != 0)
        {
            throw new FormatException("LIVE v3 STOP_RESULT payload is invalid.");
        }

        return new LiveStopResult(header.SessionId, Accepted: true, Reason: 0);
    }

    private static LiveSessionHeaderFrame ParseSessionHeaderFrame(
        LiveV3FrameHeader header,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        if (header.SessionId == 0)
        {
            throw new FormatException("LIVE v3 SESSION_HEADER session ID is invalid.");
        }

        if (payload.Length < LiveV3ProtocolConstants.SessionHeaderFixedSize)
        {
            throw new FormatException("LIVE v3 SESSION_HEADER payload is truncated.");
        }

        context.ValidateSessionId(header.SessionId);

        var boardId = payload[0];
        var streamDescriptorCount = payload[1];
        var sourceDescriptorTotalCount = payload[2];
        var omissionCount = payload[3];
        var acceptedStreamMask = (LiveStreamMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
        if (streamDescriptorCount > 6 || sourceDescriptorTotalCount > 8 || omissionCount > 16)
        {
            throw new FormatException("LIVE v3 SESSION_HEADER descriptor counts are invalid.");
        }

        var sessionStartUtc = DateTimeOffset.FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64LittleEndian(payload[8..16]));
        var sessionStartMonotonicUs = BinaryPrimitives.ReadUInt64LittleEndian(payload[16..24]);
        var descriptor = SstV5DescriptorReader.ReadDescriptorRecords(
            boardId,
            streamDescriptorCount,
            sourceDescriptorTotalCount,
            omissionCount,
            (uint)acceptedStreamMask,
            payload[LiveV3ProtocolConstants.SessionHeaderFixedSize..]);
        var admissionOmissions = CreateAdmissionOmissions(descriptor.OmissionRecords);

        context.SetSessionHeader(header.SessionId, sessionStartUtc, sessionStartMonotonicUs, descriptor);

        var acceptedSensorMask = CreateAcceptedSensorMask(descriptor);

        return new LiveSessionHeaderFrame(
            metadata,
            new LiveSessionHeader(
                SessionId: header.SessionId,
                AcceptedTravelRateMhz: GetAcceptedRateMhz(descriptor, SstV5ProtocolConstants.StreamTravel),
                AcceptedImuRateMhz: GetAcceptedRateMhz(descriptor, SstV5ProtocolConstants.StreamImu),
                AcceptedGpsRateMhz: GetAcceptedRateMhz(descriptor, SstV5ProtocolConstants.StreamGps),
                SessionStartUtc: sessionStartUtc,
                SessionStartMonotonicUs: sessionStartMonotonicUs,
                ActiveImuMask: CreateActiveImuMask(descriptor),
                ImuCalibrationScales: CreateImuCalibrationScales(descriptor),
                Flags: LiveSessionFlags.None,
                RequestedSensorMask: LiveSensorInstanceMask.None,
                AcceptedSensorMask: acceptedSensorMask,
                ProtocolVersion: LiveProtocolVersion.V3)
            {
                BoardId = boardId,
                AcceptedStreamMask = acceptedStreamMask,
                AcceptedTemperatureRateMhz = GetAcceptedRateMhz(
                    descriptor,
                    SstV5ProtocolConstants.StreamTemperature),
                AdmissionOmissions = admissionOmissions,
                StreamDescriptors = descriptor.StreamDescriptors,
            });
    }

    private static LiveTravelBatchFrame ParseTravelDataFrame(
        LiveV3FrameHeader frameHeader,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        var descriptor = GetAcceptedDataDescriptor(frameHeader, context, SstV5ProtocolConstants.StreamTravel);
        var dataHeader = SstV5DescriptorReader.ReadDataHeader(payload, descriptor);
        var decoded = SstV5CompactPayloadDecoder.DecodeTravel(
            descriptor,
            dataHeader,
            payload[LiveV3ProtocolConstants.DataHeaderSize..]);

        var records = new LiveTravelRecord[checked((int)dataHeader.SampleCount)];
        foreach (var record in decoded)
        {
            var index = checked((int)(record.Index - dataHeader.FirstIndex));
            var current = records[index];
            records[index] = record.SourceBitMask switch
            {
                SstV5ProtocolConstants.SensorForkTravel => current with { ForkAngle = record.Count },
                SstV5ProtocolConstants.SensorShockTravel => current with { ShockAngle = record.Count },
                _ => throw new FormatException("LIVE v3 travel source bit is invalid."),
            };
        }

        return new LiveTravelBatchFrame(
            metadata,
            CreateBatchHeader(frameHeader, context, LiveStreamMask.Travel, dataHeader),
            records);
    }

    private static LiveImuBatchFrame ParseImuDataFrame(
        LiveV3FrameHeader frameHeader,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        var descriptor = GetAcceptedDataDescriptor(frameHeader, context, SstV5ProtocolConstants.StreamImu);
        var dataHeader = SstV5DescriptorReader.ReadDataHeader(payload, descriptor);
        var decoded = SstV5CompactPayloadDecoder.DecodeImu(
            descriptor,
            dataHeader,
            payload[LiveV3ProtocolConstants.DataHeaderSize..]);

        var decodedBySampleAndLocation = decoded.ToDictionary(
            record => (record.Index, record.LocationId),
            record => record.Record);
        var activeSources = descriptor.Sources
            .Where(source => (dataHeader.ValidityMask & source.SourceBitMask) != 0)
            .OrderBy(source => source.SourceBitMask)
            .Select(source => new
            {
                source.SourceBitMask,
                LocationId = SstV5ProtocolConstants.GetImuLocationId(source.SourceBitMask),
            })
            .ToArray();

        var records = new List<ImuRecord>(checked((int)dataHeader.SampleCount * activeSources.Length));
        for (var sampleOffset = 0u; sampleOffset < dataHeader.SampleCount; sampleOffset++)
        {
            var sampleIndex = dataHeader.FirstIndex + sampleOffset;
            foreach (var source in activeSources)
            {
                if (decodedBySampleAndLocation.TryGetValue((sampleIndex, source.LocationId), out var record))
                {
                    records.Add(record);
                }
            }
        }

        return new LiveImuBatchFrame(
            metadata,
            CreateBatchHeader(frameHeader, context, LiveStreamMask.Imu, dataHeader),
            records);
    }

    private static LiveTemperatureBatchFrame ParseTemperatureDataFrame(
        LiveV3FrameHeader frameHeader,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        var descriptor = GetAcceptedDataDescriptor(
            frameHeader,
            context,
            SstV5ProtocolConstants.StreamTemperature);
        var dataHeader = SstV5DescriptorReader.ReadDataHeader(payload, descriptor);
        var decoded = SstV5CompactPayloadDecoder.DecodeTemperature(
            descriptor,
            dataHeader,
            context.SessionStartUtc.ToUnixTimeMilliseconds(),
            payload[LiveV3ProtocolConstants.DataHeaderSize..]);
        var records = decoded
            .Select(record => new LiveTemperatureRecord(
                record.Index,
                record.MonotonicDeltaUs,
                (LiveSensorInstanceMask)record.SourceBitMask,
                record.Sample))
            .ToArray();
        return new LiveTemperatureBatchFrame(
            metadata,
            CreateBatchHeader(frameHeader, context, LiveStreamMask.Temperature, dataHeader),
            records);
    }

    private static LiveGpsBatchFrame ParseGpsDataFrame(
        LiveV3FrameHeader frameHeader,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        var descriptor = GetAcceptedDataDescriptor(frameHeader, context, SstV5ProtocolConstants.StreamGps);
        var dataHeader = SstV5DescriptorReader.ReadDataHeader(payload, descriptor);
        var decoded = SstV5CompactPayloadDecoder.DecodeGps(
            descriptor,
            dataHeader,
            payload[LiveV3ProtocolConstants.DataHeaderSize..]);

        return new LiveGpsBatchFrame(
            metadata,
            CreateBatchHeader(frameHeader, context, LiveStreamMask.Gps, dataHeader),
            decoded.Select(record => record.Record).ToArray());
    }

    private static LiveBatteryBatchFrame ParseBatteryDataFrame(
        LiveV3FrameHeader frameHeader,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        var descriptor = GetAcceptedDataDescriptor(frameHeader, context, SstV5ProtocolConstants.StreamBattery);
        var dataHeader = SstV5DescriptorReader.ReadDataHeader(payload, descriptor);
        var decoded = SstV5CompactPayloadDecoder.DecodeBattery(
            descriptor,
            dataHeader,
            payload[LiveV3ProtocolConstants.DataHeaderSize..]);

        var records = decoded
            .Select(record => new LiveBatteryRecord(
                record.Index,
                record.MonotonicDeltaUs,
                record.Millivolts,
                record.Flags))
            .ToArray();
        return new LiveBatteryBatchFrame(
            metadata,
            CreateBatchHeader(frameHeader, context, LiveStreamMask.Battery, dataHeader),
            records);
    }

    private static LiveMarkerBatchFrame ParseMarkerDataFrame(
        LiveV3FrameHeader frameHeader,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        var descriptor = GetAcceptedDataDescriptor(frameHeader, context, SstV5ProtocolConstants.StreamMarker);
        var dataHeader = SstV5DescriptorReader.ReadDataHeader(payload, descriptor);
        var decoded = SstV5CompactPayloadDecoder.DecodeMarkers(
            descriptor,
            dataHeader,
            payload[LiveV3ProtocolConstants.DataHeaderSize..]);

        var records = decoded
            .Select(record => new LiveMarkerRecord(record.Index, record.MonotonicDeltaUs, record.MarkerType))
            .ToArray();
        return new LiveMarkerBatchFrame(
            metadata,
            CreateBatchHeader(frameHeader, context, LiveStreamMask.Marker, dataHeader),
            records);
    }

    private static IReadOnlyList<LiveStreamStatus> ParseStatus(
        LiveV3FrameHeader header,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        EnsureSessionFrame(header, context);
        if (!context.HasSessionHeader)
        {
            throw new FormatException("LIVE v3 STATUS arrived before SESSION_HEADER.");
        }
        if (payload.Length < LiveV3ProtocolConstants.StatusHeaderSize)
        {
            throw new FormatException("LIVE v3 STATUS payload is truncated.");
        }

        var statusCount = payload[0];
        if (payload[1] != 0 || BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]) != 0)
        {
            throw new FormatException("LIVE v3 STATUS reserved field is invalid.");
        }

        var expectedLength = LiveV3ProtocolConstants.StatusHeaderSize + statusCount * LiveV3ProtocolConstants.StatusRecordSize;
        if (statusCount != context.StreamDescriptorOrder.Count || payload.Length != expectedLength)
        {
            throw new FormatException("LIVE v3 STATUS payload length is invalid.");
        }

        var statuses = new LiveStreamStatus[statusCount];
        var offset = LiveV3ProtocolConstants.StatusHeaderSize;
        for (var index = 0; index < statuses.Length; index++)
        {
            var producerState = payload[offset];
            var producerFailureReason = payload[offset + 1];
            if (producerState > 2 || producerFailureReason > 4)
            {
                throw new FormatException("LIVE v3 STATUS stream record is invalid.");
            }

            statuses[index] = new LiveStreamStatus(
                Stream: ToLiveStreamMask(context.StreamDescriptorOrder[index].StreamKind),
                ProducerState: producerState,
                ProducerFailureReason: producerFailureReason,
                SinkBacklogBatches: BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + 2)..(offset + 4)]),
                ProducerMissedCount: BinaryPrimitives.ReadUInt64LittleEndian(payload[(offset + 4)..(offset + 12)]),
                ProducerMissingTimeUs: BinaryPrimitives.ReadUInt64LittleEndian(payload[(offset + 12)..(offset + 20)]),
                SinkMissedCount: BinaryPrimitives.ReadUInt64LittleEndian(payload[(offset + 20)..(offset + 28)]),
                SinkMissingTimeUs: BinaryPrimitives.ReadUInt64LittleEndian(payload[(offset + 28)..(offset + 36)]));
            offset += LiveV3ProtocolConstants.StatusRecordSize;
        }

        return statuses;
    }

    private static LiveSessionResultFrame ParseSessionResultFrame(
        LiveV3FrameHeader header,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        context.ValidateSessionId(header.SessionId);
        var finalStatus = ParseFinalStatus(payload, context.StreamDescriptorOrder);
        return new LiveSessionResultFrame(metadata, new LiveSessionResult(header.SessionId, finalStatus));
    }

    private static SstFinalStatus ParseFinalStatus(
        ReadOnlySpan<byte> payload,
        IReadOnlyList<SstV5StreamDescriptor> streamDescriptorOrder)
    {
        if (payload.Length < SstV5ProtocolConstants.FinalStatusHeaderSize)
        {
            throw new FormatException("LIVE v3 SESSION_RESULT payload is truncated.");
        }

        var sessionResultReason = payload[0];
        var streamStatusCount = payload[1];
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]);
        var stoppedMonotonicDeltaUs = BinaryPrimitives.ReadUInt64LittleEndian(payload[4..12]);

        if (sessionResultReason is not 1 and not 2 and not 4 and not 5 and not 6 ||
            flags != 0 ||
            streamStatusCount != streamDescriptorOrder.Count)
        {
            throw new FormatException("LIVE v3 SESSION_RESULT header is invalid.");
        }

        var expectedLength = SstV5ProtocolConstants.FinalStatusHeaderSize +
            streamStatusCount * SstV5ProtocolConstants.FinalStatusRecordSize;
        if (payload.Length != expectedLength)
        {
            throw new FormatException("LIVE v3 SESSION_RESULT payload length is invalid.");
        }

        var statuses = new SstStreamFinalStatus[streamStatusCount];
        var offset = SstV5ProtocolConstants.FinalStatusHeaderSize;
        for (var index = 0; index < statuses.Length; index++)
        {
            var producerState = payload[offset];
            var producerFailureReason = payload[offset + 1];
            var sinkBacklogBatches = BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + 2)..(offset + 4)]);
            if (producerState > 2 || producerFailureReason > 4)
            {
                throw new FormatException("LIVE v3 SESSION_RESULT stream status record is invalid.");
            }

            statuses[index] = new SstStreamFinalStatus
            {
                StreamKind = streamDescriptorOrder[index].StreamKind,
                ProducerState = producerState,
                ProducerFailureReason = producerFailureReason,
                SinkBacklogBatches = sinkBacklogBatches,
                ProducerMissedCount = BinaryPrimitives.ReadUInt64LittleEndian(payload[(offset + 4)..(offset + 12)]),
                ProducerMissingTimeUs = BinaryPrimitives.ReadUInt64LittleEndian(payload[(offset + 12)..(offset + 20)]),
                SinkMissedCount = BinaryPrimitives.ReadUInt64LittleEndian(payload[(offset + 20)..(offset + 28)]),
                SinkMissingTimeUs = BinaryPrimitives.ReadUInt64LittleEndian(payload[(offset + 28)..(offset + 36)]),
            };
            offset += SstV5ProtocolConstants.FinalStatusRecordSize;
        }

        return new SstFinalStatus
        {
            SessionResultReason = sessionResultReason,
            StoppedMonotonicDeltaUs = stoppedMonotonicDeltaUs,
            Streams = statuses,
        };
    }

    private static LiveV3Error ParseError(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveV3ProtocolConstants.ErrorPayloadSize, LiveV3FrameType.Error);
        if (payload[0] is < 1 or > 6 || BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]) != 0)
        {
            throw new FormatException("LIVE v3 ERROR reserved field is invalid.");
        }

        return new LiveV3Error(
            payload[0],
            payload[1],
            BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]));
    }

    private static LiveV3ErrorFrame ParseErrorFrame(
        LiveV3FrameHeader header,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        if (header.SessionId != 0)
        {
            context.ValidateSessionId(header.SessionId);
        }

        return new LiveV3ErrorFrame(metadata, ParseError(payload));
    }

    private static uint ParseNonce(ReadOnlySpan<byte> payload, LiveV3FrameType frameType)
    {
        EnsurePayloadLength(payload, LiveV3ProtocolConstants.PingPongPayloadSize, frameType);
        return BinaryPrimitives.ReadUInt32LittleEndian(payload);
    }

    private static uint ParseNonceFrame(
        LiveV3FrameHeader header,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        if (header.SessionId != 0)
        {
            context.ValidateSessionId(header.SessionId);
        }

        return ParseNonce(payload, header.FrameType);
    }

    private static LiveV3DeviceState ParseDeviceState(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < LiveV3ProtocolConstants.DeviceStateHeaderSize)
        {
            throw new FormatException("LIVE v3 DEVICE_STATE_RESP payload is truncated.");
        }

        var streamStateCount = payload[0];
        var sourceStateCount = payload[1];
        if (streamStateCount > 6 ||
            sourceStateCount > 7 ||
            BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]) != 0)
        {
            throw new FormatException("LIVE v3 DEVICE_STATE_RESP state flags are invalid.");
        }

        var expectedLength = LiveV3ProtocolConstants.DeviceStateHeaderSize +
            streamStateCount * LiveV3ProtocolConstants.StreamStateRecordSize +
            sourceStateCount * LiveV3ProtocolConstants.SourceStateRecordSize;
        if (payload.Length != expectedLength)
        {
            throw new FormatException("LIVE v3 DEVICE_STATE_RESP payload length is invalid.");
        }

        var streams = new LiveV3StreamState[streamStateCount];
        var offset = LiveV3ProtocolConstants.DeviceStateHeaderSize;
        byte previousStreamKind = 0;
        for (var index = 0; index < streams.Length; index++)
        {
            var streamKind = payload[offset];
            if (!SstV5ProtocolConstants.IsKnownStreamKind(streamKind) ||
                streamKind <= previousStreamKind ||
                payload[offset + 1] != 0 ||
                payload[offset + 2] != 0 ||
                payload[offset + 3] != 0)
            {
                throw new FormatException("LIVE v3 DEVICE_STATE_RESP stream state record is invalid.");
            }

            streams[index] = new LiveV3StreamState(
                StreamKind: streamKind,
                EffectiveDefaultRateMhz: BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 4)..(offset + 8)]),
                DefaultBatchDurationMs: BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 8)..(offset + 12)]));
            previousStreamKind = streamKind;
            offset += LiveV3ProtocolConstants.StreamStateRecordSize;
        }

        var sources = new LiveV3SourceState[sourceStateCount];
        uint previousSourceBit = 0;
        for (var index = 0; index < sources.Length; index++)
        {
            var calibrationStatus = payload[offset];
            var available = payload[offset + 1];
            var reserved = BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + 2)..(offset + 4)]);
            var sourceBitMask = BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 4)..(offset + 8)]);
            if (calibrationStatus > 2 ||
                available is not 0 and not 1 ||
                reserved != 0 ||
                !IsKnownSourceMask((LiveSensorInstanceMask)sourceBitMask) ||
                !SstV5ProtocolConstants.IsSingleBitMask(sourceBitMask) ||
                sourceBitMask <= previousSourceBit)
            {
                throw new FormatException("LIVE v3 DEVICE_STATE_RESP source state record is invalid.");
            }

            sources[index] = new LiveV3SourceState(
                CalibrationStatus: calibrationStatus,
                Available: available == 1,
                Source: (LiveSensorInstanceMask)sourceBitMask);
            previousSourceBit = sourceBitMask;
            offset += LiveV3ProtocolConstants.SourceStateRecordSize;
        }

        return new LiveV3DeviceState(streams, sources);
    }

    private static LiveV3DeviceStateFrame ParseDeviceStateFrame(
        LiveV3FrameHeader header,
        LiveFrameMetadata metadata,
        ReadOnlySpan<byte> payload)
    {
        EnsureSessionIdZero(header);
        return new LiveV3DeviceStateFrame(metadata, ParseDeviceState(payload));
    }

    private static SstV5StreamDescriptor GetAcceptedDataDescriptor(
        LiveV3FrameHeader frameHeader,
        LiveV3SessionDecodeContext context,
        byte streamKind)
    {
        EnsureSessionFrame(frameHeader, context);
        if (!context.TryGetStream(streamKind, out var descriptor))
        {
            throw new FormatException($"LIVE v3 data frame references unaccepted stream kind {streamKind}.");
        }

        return descriptor;
    }

    private static void EnsureSessionFrame(LiveV3FrameHeader header, LiveV3SessionDecodeContext context)
    {
        if (!context.HasSessionHeader)
        {
            throw new FormatException("LIVE v3 session frame arrived before SESSION_HEADER.");
        }

        context.ValidateSessionId(header.SessionId);
    }

    private static LiveBatchHeader CreateBatchHeader(
        LiveV3FrameHeader frameHeader,
        LiveV3SessionDecodeContext context,
        LiveStreamMask stream,
        SstV5DataHeader dataHeader) => new(
            SessionId: frameHeader.SessionId,
            Stream: stream,
            StreamSequence: 0,
            FirstIndex: dataHeader.FirstIndex,
            FirstMonotonicDeltaUs: dataHeader.FirstMonotonicDeltaUs,
            FirstMonotonicUs: checked(context.SessionStartMonotonicUs + dataHeader.FirstMonotonicDeltaUs),
            SampleCount: dataHeader.SampleCount,
            ValidityMask: (LiveSensorInstanceMask)dataHeader.ValidityMask);

    private static LiveProtocolFrame ParseEmptyPayloadFrame(
        LiveV3FrameHeader header,
        ReadOnlySpan<byte> payload,
        LiveProtocolFrame frame)
    {
        if (payload.Length != 0)
        {
            throw new FormatException($"{header.FrameType} should not carry a payload.");
        }

        return frame;
    }

    private static LiveProtocolFrame ParseSessionZeroEmptyPayloadFrame(
        LiveV3FrameHeader header,
        ReadOnlySpan<byte> payload,
        LiveProtocolFrame frame)
    {
        EnsureSessionIdZero(header);
        return ParseEmptyPayloadFrame(header, payload, frame);
    }

    private static void EnsureSessionIdZero(LiveV3FrameHeader header)
    {
        if (header.SessionId != 0)
        {
            throw new FormatException($"{header.FrameType} session ID must be zero.");
        }
    }

    private static void EnsurePayloadLength(ReadOnlySpan<byte> payload, int expectedLength, LiveV3FrameType frameType)
    {
        if (payload.Length != expectedLength)
        {
            throw new FormatException($"{frameType} payload length {payload.Length} does not match expected length {expectedLength}.");
        }
    }

    private static LiveStreamMask ToLiveStreamMask(byte streamKind) =>
        (LiveStreamMask)SstV5ProtocolConstants.StreamMaskForKind(streamKind);

    private static bool IsKnownStreamMask(LiveStreamMask mask)
    {
        const LiveStreamMask known =
            LiveStreamMask.Travel |
            LiveStreamMask.Imu |
            LiveStreamMask.Temperature |
            LiveStreamMask.Gps |
            LiveStreamMask.Battery |
            LiveStreamMask.Marker;
        return (mask & ~known) == 0;
    }

    private static bool IsKnownSourceMask(LiveSensorInstanceMask mask) =>
        (mask & ~LiveSensorInstanceMask.All) == 0;

    private static byte ExpectedTimingModel(byte streamKind) => streamKind switch
    {
        SstV5ProtocolConstants.StreamTravel or SstV5ProtocolConstants.StreamImu =>
            SstV5ProtocolConstants.TimingFixedRate,
        SstV5ProtocolConstants.StreamGps => SstV5ProtocolConstants.TimingGpsReceiverTimed,
        _ => SstV5ProtocolConstants.TimingMonotonicEventStatus,
    };

    private static bool IsCapabilitySourceMaskValid(
        byte streamKind,
        LiveSensorInstanceMask streamSources,
        LiveSensorInstanceMask allSupportedSources)
    {
        if (!IsKnownSourceMask(streamSources) ||
            (streamSources & ~allSupportedSources) != 0)
        {
            return false;
        }

        var allowed = streamKind switch
        {
            SstV5ProtocolConstants.StreamTravel => LiveSensorInstanceMask.Travel,
            SstV5ProtocolConstants.StreamImu or SstV5ProtocolConstants.StreamTemperature =>
                LiveSensorInstanceMask.Imu,
            SstV5ProtocolConstants.StreamGps => LiveSensorInstanceMask.Gps,
            SstV5ProtocolConstants.StreamBattery => LiveSensorInstanceMask.Battery,
            SstV5ProtocolConstants.StreamMarker => LiveSensorInstanceMask.None,
            _ => LiveSensorInstanceMask.None,
        };
        return (streamSources & ~allowed) == 0;
    }

    private static bool IsCapabilityExtensionMaskValid(byte streamKind, uint extensionMask) =>
        streamKind == SstV5ProtocolConstants.StreamGps
            ? (extensionMask & ~SstV5ProtocolConstants.ExtensionGpsDiagPublicV1) == 0
            : extensionMask == 0;

    private static uint GetAcceptedRateMhz(SstV5SessionDescriptor descriptor, byte streamKind) =>
        descriptor.TryGetStream(streamKind, out var stream) ? stream.AcceptedRateMhz : 0;

    private static LiveSensorInstanceMask CreateAcceptedSensorMask(SstV5SessionDescriptor descriptor)
    {
        var mask = LiveSensorInstanceMask.None;
        foreach (var stream in descriptor.StreamDescriptors)
        {
            mask |= (LiveSensorInstanceMask)stream.AcceptedSensorMask;
        }

        return mask;
    }

    private static IReadOnlyList<LiveStartAdmissionReason> CreateAdmissionOmissions(
        IReadOnlyList<SstV5OmissionRecord> omissions)
    {
        var results = new LiveStartAdmissionReason[omissions.Count];
        for (var index = 0; index < omissions.Count; index++)
        {
            var omission = omissions[index];
            if (!SstV5ProtocolConstants.IsKnownStreamKind(omission.StreamKind) ||
                omission.AdmissionReason is < 1 or > 8 ||
                !IsSessionOmissionTargetValid(omission))
            {
                throw new FormatException("LIVE v3 SESSION_HEADER omission record is invalid.");
            }

            results[index] = new LiveStartAdmissionReason(
                omission.AdmissionReason,
                ToLiveStreamMask(omission.StreamKind),
                omission.TargetKind == 2
                    ? (LiveSensorInstanceMask)omission.TargetMask
                    : LiveSensorInstanceMask.None,
                omission.TargetKind,
                omission.TargetMask,
                omission.StreamKind);
        }
        return results;
    }

    private static bool IsSessionOmissionTargetValid(SstV5OmissionRecord omission)
    {
        if (omission.TargetKind == 1)
        {
            return omission.TargetMask == 0;
        }
        if (omission.TargetKind == 2)
        {
            var sources = (LiveSensorInstanceMask)omission.TargetMask;
            if (sources == LiveSensorInstanceMask.None || !IsKnownSourceMask(sources))
            {
                return false;
            }
            var allowed = omission.StreamKind switch
            {
                SstV5ProtocolConstants.StreamTravel => LiveSensorInstanceMask.Travel,
                SstV5ProtocolConstants.StreamImu or SstV5ProtocolConstants.StreamTemperature =>
                    LiveSensorInstanceMask.Imu,
                SstV5ProtocolConstants.StreamGps => LiveSensorInstanceMask.Gps,
                SstV5ProtocolConstants.StreamBattery => LiveSensorInstanceMask.Battery,
                _ => LiveSensorInstanceMask.None,
            };
            return (sources & ~allowed) == 0;
        }
        return omission.TargetKind == 3 &&
               omission.StreamKind == SstV5ProtocolConstants.StreamGps &&
               omission.TargetMask != 0 &&
               (omission.TargetMask & ~SstV5ProtocolConstants.ExtensionGpsDiagPublicV1) == 0;
    }

    private static LiveImuLocationMask CreateActiveImuMask(SstV5SessionDescriptor descriptor)
    {
        if (!descriptor.TryGetStream(SstV5ProtocolConstants.StreamImu, out var imuStream))
        {
            return LiveImuLocationMask.None;
        }

        var mask = LiveImuLocationMask.None;
        if ((imuStream.AcceptedSensorMask & SstV5ProtocolConstants.SensorFrameImu) != 0) mask |= LiveImuLocationMask.Frame;
        if ((imuStream.AcceptedSensorMask & SstV5ProtocolConstants.SensorForkImu) != 0) mask |= LiveImuLocationMask.Fork;
        if ((imuStream.AcceptedSensorMask & SstV5ProtocolConstants.SensorRearImu) != 0) mask |= LiveImuLocationMask.Rear;
        return mask;
    }

    private static LiveImuCalibrationScales CreateImuCalibrationScales(SstV5SessionDescriptor descriptor)
    {
        var frameAccel = 0f;
        var forkAccel = 0f;
        var rearAccel = 0f;
        var frameGyro = 0f;
        var forkGyro = 0f;
        var rearGyro = 0f;

        if (descriptor.TryGetStream(SstV5ProtocolConstants.StreamImu, out var imuStream))
        {
            foreach (var source in imuStream.Sources)
            {
                switch (source.SourceBitMask)
                {
                    case SstV5ProtocolConstants.SensorFrameImu:
                        frameAccel = source.AccelLsbPerG;
                        frameGyro = source.GyroLsbPerDps;
                        break;
                    case SstV5ProtocolConstants.SensorForkImu:
                        forkAccel = source.AccelLsbPerG;
                        forkGyro = source.GyroLsbPerDps;
                        break;
                    case SstV5ProtocolConstants.SensorRearImu:
                        rearAccel = source.AccelLsbPerG;
                        rearGyro = source.GyroLsbPerDps;
                        break;
                }
            }
        }

        return new LiveImuCalibrationScales(frameAccel, forkAccel, rearAccel, frameGyro, forkGyro, rearGyro);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> payload, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, sizeof(uint)));
        offset += sizeof(uint);
        return value;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> payload, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset, sizeof(ushort)));
        offset += sizeof(ushort);
        return value;
    }

    private static void WriteUInt16(Span<byte> payload, ref int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(offset, sizeof(ushort)), value);
        offset += sizeof(ushort);
    }

    private static void WriteUInt32(Span<byte> payload, ref int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(offset, sizeof(uint)), value);
        offset += sizeof(uint);
    }
}

public sealed class LiveV3SessionDecodeContext
{
    private readonly Dictionary<byte, SstV5StreamDescriptor> streamsByKind = [];

    public byte? AcceptedSessionId { get; private set; }
    public DateTimeOffset SessionStartUtc { get; private set; }
    public ulong SessionStartMonotonicUs { get; private set; }
    public IReadOnlyList<SstV5StreamDescriptor> StreamDescriptors { get; private set; } = [];
    public IReadOnlyList<SstV5SourceDescriptor> SourceDescriptors { get; private set; } = [];
    public IReadOnlyList<SstV5StreamDescriptor> StreamDescriptorOrder => StreamDescriptors;
    public bool HasSessionHeader { get; private set; }

    public void Reset()
    {
        AcceptedSessionId = null;
        SessionStartUtc = default;
        SessionStartMonotonicUs = 0;
        StreamDescriptors = [];
        SourceDescriptors = [];
        HasSessionHeader = false;
        streamsByKind.Clear();
    }

    public void AcceptSession(byte sessionId)
    {
        if (AcceptedSessionId is { } acceptedSessionId && acceptedSessionId != sessionId)
        {
            throw new FormatException("LIVE v3 session ID changed after START_RESULT.");
        }

        AcceptedSessionId = sessionId;
    }

    public void SetSessionHeader(
        byte sessionId,
        DateTimeOffset sessionStartUtc,
        ulong sessionStartMonotonicUs,
        SstV5SessionDescriptor descriptor)
    {
        ValidateSessionId(sessionId);
        AcceptedSessionId ??= sessionId;
        SessionStartUtc = sessionStartUtc;
        SessionStartMonotonicUs = sessionStartMonotonicUs;
        StreamDescriptors = descriptor.StreamDescriptors;
        SourceDescriptors = descriptor.StreamDescriptors.SelectMany(stream => stream.Sources).ToArray();
        HasSessionHeader = true;

        streamsByKind.Clear();
        foreach (var stream in descriptor.StreamDescriptors)
        {
            streamsByKind.Add(stream.StreamKind, stream);
        }
    }

    public bool TryGetStream(byte streamKind, out SstV5StreamDescriptor descriptor) =>
        streamsByKind.TryGetValue(streamKind, out descriptor!);

    public void ValidateSessionId(byte sessionId)
    {
        if (sessionId == 0)
        {
            throw new FormatException("LIVE v3 session frame session ID is invalid.");
        }

        if (AcceptedSessionId is { } acceptedSessionId && acceptedSessionId != sessionId)
        {
            throw new FormatException("LIVE v3 session frame session ID does not match the accepted session.");
        }
    }
}
