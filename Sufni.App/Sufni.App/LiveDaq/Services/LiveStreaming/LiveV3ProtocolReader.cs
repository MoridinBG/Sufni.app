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
    private readonly LiveV3SessionDecodeContext context = new();

    public int BufferedByteCount => frameReader.BufferedByteCount;
    public LiveV3SessionDecodeContext Context => context;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        frameReader.Append(bytes);
    }

    public void Reset()
    {
        frameReader.Reset();
        context.Reset();
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

        var payload = new byte[
            LiveV3ProtocolConstants.StartRequestHeaderSize +
            streamRequests.Count * LiveV3ProtocolConstants.StreamRequestRecordSize];
        var offset = 0;
        payload[offset++] = checked((byte)streamRequests.Count);
        payload[offset++] = 0;
        WriteUInt16(payload, ref offset, request.StartFlags);

        byte previousStreamKind = 0;
        foreach (var record in streamRequests)
        {
            if (!SstV5ProtocolConstants.IsKnownStreamKind(record.StreamKind) ||
                record.StreamKind <= previousStreamKind)
            {
                throw new ArgumentException("LIVE v3 START_REQ stream records must use known stream kinds in ascending order.", nameof(request));
            }

            payload[offset++] = record.StreamKind;
            payload[offset++] = 0;
            WriteUInt16(payload, ref offset, record.RecordFlags);
            WriteUInt32(payload, ref offset, (uint)record.SourceMask);
            WriteUInt32(payload, ref offset, record.ExtensionMask);
            WriteUInt32(payload, ref offset, record.RateMhz);
            WriteUInt32(payload, ref offset, record.BatchDurationMs);
            previousStreamKind = record.StreamKind;
        }

        return CreateFrame(LiveV3FrameType.StartReq, sessionId: 0, sequence, payload);
    }

    public static byte[] CreateStopRequestFrame(byte sessionId, uint sequence) =>
        CreateFrame(LiveV3FrameType.StopReq, sessionId, sequence, ReadOnlySpan<byte>.Empty);

    public static byte[] CreatePingFrame(byte sessionId, uint sequence) =>
        CreateFrame(LiveV3FrameType.Ping, sessionId, sequence, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateDeviceStateRequestFrame(uint sequence) =>
        CreateFrame(LiveV3FrameType.DeviceStateReq, sessionId: 0, sequence, ReadOnlySpan<byte>.Empty);

    public static LiveV3FrameHeader ParseHeader(ReadOnlySpan<byte> headerBytes, bool allowUnknownFrameType = false)
    {
        if (headerBytes.Length < LiveV3ProtocolConstants.FrameHeaderSize)
        {
            throw new FormatException("LIVE v3 frame header is truncated.");
        }

        var rawFrameType = headerBytes[0];
        var frameType = (LiveV3FrameType)rawFrameType;
        if (!allowUnknownFrameType && !IsKnownFrameType(frameType))
        {
            throw new FormatException($"LIVE v3 frame type {rawFrameType} is invalid.");
        }

        var header = new LiveV3FrameHeader(
            FrameType: frameType,
            Flags: headerBytes[1],
            SessionId: headerBytes[2],
            Reserved: headerBytes[3],
            PayloadLength: BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[4..8]),
            TxSequence: BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[8..12]));

        if (header.Flags != 0)
        {
            throw new FormatException("LIVE v3 frame flags are invalid.");
        }

        if (header.Reserved != 0)
        {
            throw new FormatException("LIVE v3 frame reserved field is invalid.");
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
            LiveV3FrameType.CapabilitiesReq => ParseEmptyPayloadFrame(header, payload, new LiveV3CapabilitiesRequestFrame(metadata)),
            LiveV3FrameType.CapabilitiesResp => new LiveV3CapabilitiesFrame(metadata, ParseCapabilities(payload)),
            LiveV3FrameType.StartReq => new LiveV3StartRequestFrame(metadata, ParseStartRequest(payload)),
            LiveV3FrameType.StartResult => ParseStartResultFrame(header, metadata, payload, context),
            LiveV3FrameType.StopReq => ParseEmptyPayloadFrame(header, payload, new LiveStopRequestFrame(metadata)),
            LiveV3FrameType.StopResult => new LiveStopResultFrame(metadata, ParseStopResult(header, payload, context)),
            LiveV3FrameType.SessionHeader => ParseSessionHeaderFrame(header, metadata, payload, context),
            LiveV3FrameType.SessionResult => ParseSessionResultFrame(header, metadata, payload, context),
            LiveV3FrameType.TravelData => ParseTravelDataFrame(header, metadata, payload, context),
            LiveV3FrameType.ImuData => ParseImuDataFrame(header, metadata, payload, context),
            LiveV3FrameType.TemperatureData => ParseTemperatureDataFrame(header, payload, context),
            LiveV3FrameType.GpsData => ParseGpsDataFrame(header, metadata, payload, context),
            LiveV3FrameType.BatteryData => ParseBatteryDataFrame(header, metadata, payload, context),
            LiveV3FrameType.MarkerData => ParseMarkerDataFrame(header, metadata, payload, context),
            LiveV3FrameType.Status => new LiveStatusFrame(metadata, ParseStatus(header, payload, context)),
            LiveV3FrameType.Ping => ParseEmptyPayloadFrame(header, payload, new LivePingFrame(metadata)),
            LiveV3FrameType.Pong => ParseEmptyPayloadFrame(header, payload, new LivePongFrame(metadata)),
            LiveV3FrameType.Error => new LiveV3ErrorFrame(metadata, ParseError(payload)),
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
        frame[0] = (byte)frameType;
        frame[1] = 0;
        frame[2] = sessionId;
        frame[3] = 0;
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

        return new LiveV3StartRequest
        {
            StartFlags = startFlags,
            StreamRequests = records,
        };
    }

    private static LiveV3Capabilities ParseCapabilities(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < LiveV3ProtocolConstants.CapabilitiesHeaderSize)
        {
            throw new FormatException("LIVE v3 capabilities payload is truncated.");
        }

        var maxFramePayloadBytes = BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]);
        var supportedStreamMask = (LiveStreamMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
        var supportedSourceMask = (LiveSensorInstanceMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]);
        var supportedExtensionMask = BinaryPrimitives.ReadUInt32LittleEndian(payload[12..16]);
        var streamCount = payload[16];
        if (payload[17] != 0 || payload[18] != 0 || payload[19] != 0)
        {
            throw new FormatException("LIVE v3 capabilities reserved field is invalid.");
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
        for (var index = 0; index < streams.Length; index++)
        {
            var streamKind = payload[offset];
            if (!SstV5ProtocolConstants.IsKnownStreamKind(streamKind) ||
                payload[offset + 1] != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + 2)..(offset + 4)]) != 0)
            {
                throw new FormatException("LIVE v3 stream capability record is invalid.");
            }

            streams[index] = new LiveV3StreamCapability(
                Stream: ToLiveStreamMask(streamKind),
                SupportedSourceMask: (LiveSensorInstanceMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 4)..(offset + 8)]),
                SupportedExtensionMask: BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 8)..(offset + 12)]),
                MinRateMhz: BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 12)..(offset + 16)]),
                MaxRateMhz: BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 16)..(offset + 20)]));
            offset += LiveV3ProtocolConstants.StreamCapabilityRecordSize;
        }

        return new LiveV3Capabilities(
            maxFramePayloadBytes,
            supportedStreamMask,
            supportedSourceMask,
            supportedExtensionMask,
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
        var sessionId = payload[1];
        var admissionReasonCount = payload[2];
        if (payload[3] != 0)
        {
            throw new FormatException("LIVE v3 START_RESULT reserved field is invalid.");
        }

        if (resultCode is not 0 and not 1)
        {
            throw new FormatException("LIVE v3 START_RESULT code is invalid.");
        }

        if (resultCode == 0 && sessionId == 0)
        {
            throw new FormatException("LIVE v3 START_RESULT accepted session ID is invalid.");
        }

        var expectedLength = LiveV3ProtocolConstants.StartResultHeaderSize +
            admissionReasonCount * LiveV3ProtocolConstants.AdmissionReasonRecordSize;
        if (payload.Length != expectedLength)
        {
            throw new FormatException("LIVE v3 START_RESULT payload length is invalid.");
        }

        var acceptedStreamMask = (LiveStreamMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
        var reasons = new LiveStartAdmissionReason[admissionReasonCount];
        var offset = LiveV3ProtocolConstants.StartResultHeaderSize;
        for (var index = 0; index < reasons.Length; index++)
        {
            var targetKind = payload[offset];
            var streamKind = payload[offset + 1];
            var reason = payload[offset + 2];
            var reserved = payload[offset + 3];
            var targetMask = BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 4)..(offset + 8)]);

            if (targetKind is < 1 or > 3 ||
                reserved != 0 ||
                (streamKind != 0 && !SstV5ProtocolConstants.IsKnownStreamKind(streamKind)))
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
                targetMask);
            offset += LiveV3ProtocolConstants.AdmissionReasonRecordSize;
        }

        if (resultCode == 0)
        {
            context.AcceptSession(sessionId);
        }

        return new LiveV3StartResultFrame(
            metadata,
            new LiveV3StartResult(resultCode, sessionId, acceptedStreamMask, reasons));
    }

    private static LiveStopResult ParseStopResult(
        LiveV3FrameHeader header,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        EnsureSessionFrame(header, context);
        EnsurePayloadLength(payload, LiveV3ProtocolConstants.StopResultPayloadSize, LiveV3FrameType.StopResult);
        if (BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]) != 0)
        {
            throw new FormatException("LIVE v3 STOP_RESULT reserved field is invalid.");
        }

        var result = new LiveV3StopResult(payload[0], payload[1]);
        return new LiveStopResult(header.SessionId, result.Accepted, result.Reason);
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

        var flags = (LiveSessionFlags)BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]);
        var requestedSensorMask = (LiveSensorInstanceMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]);
        var sessionStartUtc = DateTimeOffset.FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64LittleEndian(payload[8..16]));
        var sessionStartMonotonicUs = BinaryPrimitives.ReadUInt64LittleEndian(payload[16..24]);
        var descriptor = SstV5DescriptorReader.ReadSessionDescriptor(payload[LiveV3ProtocolConstants.SessionHeaderFixedSize..]);

        context.SetSessionHeader(header.SessionId, sessionStartUtc, sessionStartMonotonicUs, descriptor);

        var acceptedSensorMask = CreateAcceptedSensorMask(descriptor);
        if (requestedSensorMask == LiveSensorInstanceMask.None)
        {
            requestedSensorMask = acceptedSensorMask;
        }

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
                Flags: flags,
                RequestedSensorMask: requestedSensorMask,
                AcceptedSensorMask: acceptedSensorMask,
                ProtocolVersion: LiveProtocolVersion.V3));
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

    private static LiveProtocolFrame ParseTemperatureDataFrame(
        LiveV3FrameHeader frameHeader,
        ReadOnlySpan<byte> payload,
        LiveV3SessionDecodeContext context)
    {
        EnsureSessionFrame(frameHeader, context);
        if (!context.TryGetStream(SstV5ProtocolConstants.StreamTemperature, out _))
        {
            throw new FormatException("LIVE v3 temperature data arrived without an accepted temperature descriptor.");
        }

        throw new FormatException("LIVE v3 temperature data is not supported by the live app.");
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
        if (payload.Length != expectedLength)
        {
            throw new FormatException("LIVE v3 STATUS payload length is invalid.");
        }

        var statuses = new LiveStreamStatus[statusCount];
        var offset = LiveV3ProtocolConstants.StatusHeaderSize;
        for (var index = 0; index < statuses.Length; index++)
        {
            var streamKind = payload[offset];
            if (!SstV5ProtocolConstants.IsKnownStreamKind(streamKind))
            {
                throw new FormatException("LIVE v3 STATUS stream record is invalid.");
            }

            statuses[index] = new LiveStreamStatus(
                Stream: ToLiveStreamMask(streamKind),
                ProducerState: payload[offset + 1],
                ProducerFailureReason: 0,
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
        if (payload[1] != 0 || BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]) != 0)
        {
            throw new FormatException("LIVE v3 ERROR reserved field is invalid.");
        }

        return new LiveV3Error(payload[0]);
    }

    private static LiveV3DeviceState ParseDeviceState(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < LiveV3ProtocolConstants.DeviceStateHeaderSize)
        {
            throw new FormatException("LIVE v3 DEVICE_STATE_RESP payload is truncated.");
        }

        var streamStateCount = payload[0];
        var sourceStateCount = payload[1];
        if (BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]) != 0)
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
            if (available is not 0 and not 1 ||
                reserved != 0 ||
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
