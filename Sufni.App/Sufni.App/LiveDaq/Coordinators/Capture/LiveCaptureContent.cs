using System.Collections.Generic;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

internal readonly record struct LiveCaptureContent(
    Metadata Metadata,
    LiveSessionHeader? SessionHeader,
    FixedRateSegment<ushort>[] FrontTravelSegments,
    FixedRateSegment<ushort>[] RearTravelSegments,
    IReadOnlyDictionary<LiveImuLocation, FixedRateSegment<ImuRecord>[]> ImuSegmentsByLocation,
    ChunkedBufferSnapshot<GpsRecord> GpsRecords,
    ChunkedBufferSnapshot<TemperatureSample> TemperatureSamples,
    MarkerData[] Markers,
    RawStreamGap[] StreamGaps,
    SstFinalStatus? FinalStatus,
    bool MissingFinalStatus);
