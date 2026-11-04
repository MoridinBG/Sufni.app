using System.Collections.Generic;
using System.Linq;
using System;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

internal sealed record AcceptedTravelBatch(double[] Times, ushort[]? FrontCounts, ushort[]? RearCounts);

internal sealed record AcceptedImuSeries(
    LiveImuLocation Location, IReadOnlyList<double> Times, IReadOnlyList<ImuRecord> Records);

internal sealed class LiveCaptureRecorder
{
    private const int GpsChunkSize = 256;
    private const int TemperatureChunkSize = 64;
    private readonly AppendOnlyChunkBuffer<GpsRecord> gpsRecords = new(GpsChunkSize);
    private readonly AppendOnlyChunkBuffer<TemperatureSample> temperatureSamples = new(TemperatureChunkSize);
    private readonly List<MarkerData> markers = [];
    private readonly List<RawStreamGap> streamGaps = [];
    private readonly FixedRateSegmentBuilder<ushort> frontTravelBuilder;
    private readonly FixedRateSegmentBuilder<ushort> rearTravelBuilder;
    private readonly Dictionary<LiveImuLocation, FixedRateSegmentBuilder<ImuRecord>> imuBuilders = [];

    private SstFinalStatus? finalStatus;
    private ulong? captureStartMonotonicUs;
    private ulong? latestGpsMonotonicUs;

    public DateTimeOffset? CaptureStartUtc { get; private set; }
    public bool HasTravelData => frontTravelBuilder.Count > 0 || rearTravelBuilder.Count > 0;
    public bool HasImuData => imuBuilders.Values.Any(builder => builder.Count > 0);
    public bool CanSave => frontTravelBuilder.Count >= 5 || rearTravelBuilder.Count >= 5;
    public bool HasAnyCapture => frontTravelBuilder.Count > 0 ||
        rearTravelBuilder.Count > 0 ||
        imuBuilders.Values.Any(builder => builder.Count > 0) ||
        gpsRecords.Count > 0 || temperatureSamples.Count > 0 || markers.Count > 0;

    public LiveCaptureRecorder()
    {
        frontTravelBuilder = new FixedRateSegmentBuilder<ushort>(
            SstV5ProtocolConstants.StreamTravel,
            (byte)SstV5ProtocolConstants.SensorForkTravel,
            AddStreamGap);
        rearTravelBuilder = new FixedRateSegmentBuilder<ushort>(
            SstV5ProtocolConstants.StreamTravel,
            (byte)SstV5ProtocolConstants.SensorShockTravel,
            AddStreamGap);
    }

    public void Reset()
    {
        frontTravelBuilder.Clear();
        rearTravelBuilder.Clear();
        foreach (var builder in imuBuilders.Values)
        {
            builder.Clear();
        }

        gpsRecords.Clear();
        temperatureSamples.Clear();
        markers.Clear();
        streamGaps.Clear();
        finalStatus = null;
        captureStartMonotonicUs = null;
        latestGpsMonotonicUs = null;
        CaptureStartUtc = null;
    }

    public void ResetImuLocations(LiveSessionHeader? sessionHeader)
    {
        imuBuilders.Clear();
        EnsureImuLocations(sessionHeader);
    }

    public void RecordFinalStatus(SstFinalStatus status) => finalStatus = status;

    public AcceptedTravelBatch? AppendTravel(LiveTravelBatchFrame frame, LiveSessionHeader? sessionHeader)
    {
        if (sessionHeader is null || frame.Records.Count == 0)
        {
            return null;
        }

        var batchCount = frame.Records.Count;
        var travelTimes = new double[batchCount];
        var frontCounts = new ushort[batchCount];
        var rearCounts = new ushort[batchCount];
        var firstDeltaUs = GetFirstMonotonicDeltaUs(frame.Batch, sessionHeader);
        var effectiveValidityMask = frame.Batch.ValidityMask & sessionHeader.AcceptedSensorMask;
        var frontAccepted = sessionHeader.AcceptedSensorMask.HasFlag(LiveSensorInstanceMask.ForkTravel);
        var rearAccepted = sessionHeader.AcceptedSensorMask.HasFlag(LiveSensorInstanceMask.ShockTravel);
        var frontValid = frontAccepted && effectiveValidityMask.HasFlag(LiveSensorInstanceMask.ForkTravel);
        var rearValid = rearAccepted && effectiveValidityMask.HasFlag(LiveSensorInstanceMask.ShockTravel);

        if (frontAccepted && !frontValid)
        {
            frontTravelBuilder.AddInvalidRange(
                frame.Batch.FirstIndex,
                frame.Batch.SampleCount,
                firstDeltaUs,
                sessionHeader.AcceptedTravelRateMhz);
        }

        if (rearAccepted && !rearValid)
        {
            rearTravelBuilder.AddInvalidRange(
                frame.Batch.FirstIndex,
                frame.Batch.SampleCount,
                firstDeltaUs,
                sessionHeader.AcceptedTravelRateMhz);
        }

        InitializeCaptureOrigin(frame.Batch.FirstMonotonicUs, sessionHeader);

        for (var index = 0; index < batchCount; index++)
        {
            var record = frame.Records[index];
            var monotonicUs = frame.Batch.FirstMonotonicUs + (ulong)index * sessionHeader.TravelPeriodUs;
            var monotonicDeltaUs = firstDeltaUs + SstV5CompactPayloadDecoder.RoundDurationUs((ulong)index, sessionHeader.AcceptedTravelRateMhz);
            var sampleIndex = frame.Batch.FirstIndex + (ulong)index;
            var timeOffset = ToSampleOffsetSeconds(monotonicUs, sessionHeader);
            travelTimes[index] = timeOffset;

            if (frontValid)
            {
                frontTravelBuilder.AddValidSample(sampleIndex, monotonicDeltaUs, record.ForkAngle, sessionHeader.AcceptedTravelRateMhz);
                frontCounts[index] = record.ForkAngle;
            }

            if (rearValid)
            {
                rearTravelBuilder.AddValidSample(sampleIndex, monotonicDeltaUs, record.ShockAngle, sessionHeader.AcceptedTravelRateMhz);
                rearCounts[index] = record.ShockAngle;
            }
        }

        return new AcceptedTravelBatch(travelTimes, frontValid ? frontCounts : null, rearValid ? rearCounts : null);
    }

    public IReadOnlyList<AcceptedImuSeries>? AppendImu(LiveImuBatchFrame frame, LiveSessionHeader? sessionHeader)
    {
        if (sessionHeader is null || frame.Records.Count == 0)
        {
            return null;
        }

        var activeLocations = sessionHeader.GetActiveImuLocations();
        if (activeLocations.Count == 0)
        {
            return null;
        }

        EnsureImuLocations(sessionHeader);
        var firstDeltaUs = GetFirstMonotonicDeltaUs(frame.Batch, sessionHeader);
        var effectiveValidityMask = frame.Batch.ValidityMask & sessionHeader.AcceptedSensorMask;
        var validLocations = new List<LiveImuLocation>(activeLocations.Count);
        foreach (var location in activeLocations)
        {
            var sourceMask = GetImuSourceMask(location);
            if ((effectiveValidityMask & sourceMask) == 0)
            {
                if (imuBuilders.TryGetValue(location, out var builder))
                {
                    builder.AddInvalidRange(
                        frame.Batch.FirstIndex,
                        frame.Batch.SampleCount,
                        firstDeltaUs,
                        sessionHeader.AcceptedImuRateMhz);
                }
            }
            else
            {
                validLocations.Add(location);
            }
        }

        InitializeCaptureOrigin(frame.Batch.FirstMonotonicUs, sessionHeader);

        var tickCount = (int)frame.Batch.SampleCount;
        var recordsPerTick = activeLocations.Count;
        var perLocationTimes = new double[recordsPerTick][];
        var perLocationRecords = new ImuRecord[recordsPerTick][];
        var perLocationCounts = new int[recordsPerTick];
        for (var locationIndex = 0; locationIndex < recordsPerTick; locationIndex++)
        {
            perLocationTimes[locationIndex] = new double[tickCount];
            perLocationRecords[locationIndex] = new ImuRecord[tickCount];
        }

        for (var tickIndex = 0; tickIndex < tickCount; tickIndex++)
        {
            var timeOffset = ToSampleOffsetSeconds(
                frame.Batch.FirstMonotonicUs + (ulong)tickIndex * sessionHeader.ImuPeriodUs, sessionHeader);

            for (var locationIndex = 0; locationIndex < recordsPerTick; locationIndex++)
            {
                var location = activeLocations[locationIndex];
                var sourceMask = GetImuSourceMask(location);
                if ((effectiveValidityMask & sourceMask) == 0)
                {
                    continue;
                }

                var validLocationIndex = validLocations.IndexOf(location);
                var recordIndex = tickIndex * validLocations.Count + validLocationIndex;
                if (recordIndex >= frame.Records.Count)
                {
                    break;
                }

                var nextIndex = perLocationCounts[locationIndex]++;
                var record = frame.Records[recordIndex];
                perLocationTimes[locationIndex][nextIndex] = timeOffset;
                perLocationRecords[locationIndex][nextIndex] = record;
                imuBuilders[location].AddValidSample(
                    frame.Batch.FirstIndex + (ulong)tickIndex,
                    firstDeltaUs + SstV5CompactPayloadDecoder.RoundDurationUs((ulong)tickIndex, sessionHeader.AcceptedImuRateMhz),
                    record,
                    sessionHeader.AcceptedImuRateMhz);
            }
        }

        var inputSeries = new List<AcceptedImuSeries>(activeLocations.Count);
        for (var locationIndex = 0; locationIndex < activeLocations.Count; locationIndex++)
        {
            var location = activeLocations[locationIndex];
            var sampleCount = perLocationCounts[locationIndex];
            inputSeries.Add(new AcceptedImuSeries(
                location,
                TrimSeries(perLocationTimes[locationIndex], sampleCount),
                TrimSeries(perLocationRecords[locationIndex], sampleCount)));
        }

        return inputSeries;

        static IReadOnlyList<T> TrimSeries<T>(T[] values, int count)
        {
            if (count == values.Length)
            {
                return values;
            }

            if (count == 0)
            {
                return Array.Empty<T>();
            }

            var trimmed = new T[count];
            Array.Copy(values, trimmed, count);
            return trimmed;
        }
    }

    public bool AppendGps(LiveGpsBatchFrame frame, LiveSessionHeader? sessionHeader)
    {
        if (frame.Records.Count == 0) return false;
        InitializeCaptureOrigin(frame.Batch.FirstMonotonicUs, sessionHeader);
        latestGpsMonotonicUs = Math.Max(latestGpsMonotonicUs ?? 0, frame.Batch.FirstMonotonicUs);
        gpsRecords.AppendRange(frame.Records);
        return true;
    }

    public ChunkedBufferSnapshot<GpsRecord> CreateGpsSnapshot() => gpsRecords.CreateSnapshot();

    public bool AppendTemperature(LiveTemperatureBatchFrame frame, LiveSessionHeader? sessionHeader)
    {
        if (frame.Records.Count == 0)
        {
            return false;
        }

        InitializeCaptureOrigin(frame.Batch.FirstMonotonicUs, sessionHeader);
        foreach (var record in frame.Records)
        {
            temperatureSamples.Append(record.Sample);
        }
        return true;
    }

    public bool AppendMarkers(LiveMarkerBatchFrame frame)
    {
        foreach (var record in frame.Records)
        {
            if (record.MarkerType == SstV5ProtocolConstants.MarkerManualUserMark)
            {
                markers.Add(new MarkerData(record.MonotonicDeltaUs / 1_000_000.0));
            }
        }

        return frame.Records.Count > 0;
    }

    public void EnsureImuLocations(LiveSessionHeader? sessionHeader)
    {
        if (sessionHeader is null)
        {
            return;
        }

        foreach (var location in sessionHeader.GetActiveImuLocations())
        {
            if (imuBuilders.ContainsKey(location))
            {
                continue;
            }

            imuBuilders[location] = new FixedRateSegmentBuilder<ImuRecord>(
                SstV5ProtocolConstants.StreamImu,
                (byte)location,
                AddStreamGap);
        }
    }

    private void AddStreamGap(RawStreamGap gap)
    {
        streamGaps.Add(gap);
    }

    private static ulong GetFirstMonotonicDeltaUs(LiveBatchHeader batch, LiveSessionHeader? sessionHeader)
    {
        if (sessionHeader is null)
        {
            return batch.FirstMonotonicDeltaUs;
        }

        return batch.FirstMonotonicUs >= sessionHeader.SessionStartMonotonicUs
            ? batch.FirstMonotonicUs - sessionHeader.SessionStartMonotonicUs
            : batch.FirstMonotonicDeltaUs;
    }

    private static LiveSensorInstanceMask GetImuSourceMask(LiveImuLocation location) => location switch
    {
        LiveImuLocation.Frame => LiveSensorInstanceMask.FrameImu,
        LiveImuLocation.Fork => LiveSensorInstanceMask.ForkImu,
        LiveImuLocation.Rear => LiveSensorInstanceMask.RearImu,
        _ => LiveSensorInstanceMask.None,
    };

    public LiveCaptureContent CreateContent(string displayName, LiveSessionHeader? sessionHeader, bool captureClosed)
    {
        return new LiveCaptureContent(
            Metadata: new Metadata
            {
                SourceName = displayName,
                Version = 4,
                SampleRate = (int)(sessionHeader?.AcceptedTravelHz ?? 0),
                Timestamp = (CaptureStartUtc ?? sessionHeader?.SessionStartUtc ?? DateTimeOffset.UnixEpoch).ToUnixTimeSeconds(),
                Duration = CalculateDuration(sessionHeader).TotalSeconds,
            },
            SessionHeader: sessionHeader,
            FrontTravelSegments: frontTravelBuilder.CreateSnapshot(),
            RearTravelSegments: rearTravelBuilder.CreateSnapshot(),
            ImuSegmentsByLocation: imuBuilders.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.CreateSnapshot()),
            GpsRecords: gpsRecords.CreateSnapshot(),
            TemperatureSamples: temperatureSamples.CreateSnapshot(),
            Markers: [.. markers],
            StreamGaps: [.. streamGaps],
            FinalStatus: finalStatus,
            MissingFinalStatus: sessionHeader?.ExpectsFinalStatus == true &&
                finalStatus is null &&
                captureClosed);
    }

    private double ToSampleOffsetSeconds(ulong sampleMonotonicUs, LiveSessionHeader? sessionHeader)
    {
        if (sessionHeader is null)
        {
            return 0;
        }

        var captureStartUs = captureStartMonotonicUs ?? sessionHeader.SessionStartMonotonicUs;
        var deltaUs = sampleMonotonicUs >= captureStartUs
            ? sampleMonotonicUs - captureStartUs
            : 0;
        return deltaUs / 1_000_000.0;
    }

    public TimeSpan CalculateDuration(LiveSessionHeader? sessionHeader)
    {
        if (captureStartMonotonicUs is not { } captureOriginUs)
        {
            return TimeSpan.Zero;
        }

        if (finalStatus is not null && sessionHeader is not null)
        {
            var captureStartDeltaUs = captureOriginUs >= sessionHeader.SessionStartMonotonicUs
                ? captureOriginUs - sessionHeader.SessionStartMonotonicUs
                : 0;
            var durationUs = finalStatus.StoppedMonotonicDeltaUs >= captureStartDeltaUs
                ? finalStatus.StoppedMonotonicDeltaUs - captureStartDeltaUs
                : 0;
            return TimeSpan.FromMilliseconds(durationUs / 1000.0);
        }

        var travelEndDeltaUs = GetLatestTravelEndMonotonicDeltaUs(sessionHeader);
        if (travelEndDeltaUs is not null && sessionHeader is not null)
        {
            var captureStartDeltaUs = captureOriginUs >= sessionHeader.SessionStartMonotonicUs
                ? captureOriginUs - sessionHeader.SessionStartMonotonicUs
                : 0;
            var durationUs = travelEndDeltaUs.Value >= captureStartDeltaUs
                ? travelEndDeltaUs.Value - captureStartDeltaUs
                : 0;
            return TimeSpan.FromMilliseconds(durationUs / 1000.0);
        }

        if (latestGpsMonotonicUs is { } gpsEndUs)
        {
            var durationUs = gpsEndUs >= captureOriginUs
                ? gpsEndUs - captureOriginUs : 0;
            return TimeSpan.FromMilliseconds(durationUs / 1000.0);
        }

        return TimeSpan.Zero;
    }

    private ulong? GetLatestTravelEndMonotonicDeltaUs(LiveSessionHeader? sessionHeader)
    {
        if (sessionHeader is null || sessionHeader.AcceptedTravelRateMhz == 0)
        {
            return null;
        }

        var latestEndMonotonicDeltaUs = frontTravelBuilder.LatestEndMonotonicDeltaUs;
        if (rearTravelBuilder.LatestEndMonotonicDeltaUs is ulong rearEndMonotonicDeltaUs &&
            (latestEndMonotonicDeltaUs is null || rearEndMonotonicDeltaUs > latestEndMonotonicDeltaUs.Value))
        {
            latestEndMonotonicDeltaUs = rearEndMonotonicDeltaUs;
        }

        return latestEndMonotonicDeltaUs;
    }

    private void InitializeCaptureOrigin(ulong sampleMonotonicUs, LiveSessionHeader? sessionHeader)
    {
        if (sessionHeader is null || captureStartMonotonicUs is not null)
        {
            return;
        }

        captureStartMonotonicUs = sampleMonotonicUs;
        var deltaUs = sampleMonotonicUs >= sessionHeader.SessionStartMonotonicUs
            ? sampleMonotonicUs - sessionHeader.SessionStartMonotonicUs
            : 0;
        CaptureStartUtc = sessionHeader.SessionStartUtc.AddMilliseconds(deltaUs / 1000.0);
    }
}
