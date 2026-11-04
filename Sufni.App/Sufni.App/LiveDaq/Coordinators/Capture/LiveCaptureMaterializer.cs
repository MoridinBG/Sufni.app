using System.Collections.Generic;
using System.Linq;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

internal static class LiveCaptureMaterializer
{
    public static LiveTelemetryCapture Materialize(LiveCaptureContent snapshot, BikeData bikeData)
    {
        return new LiveTelemetryCapture(
            Metadata: snapshot.Metadata,
            BikeData: bikeData,
            FrontSegments: ToRawCountSegments(snapshot.FrontTravelSegments),
            RearSegments: ToRawCountSegments(snapshot.RearTravelSegments),
            ImuData: BuildImuCapture(snapshot),
            GpsData: snapshot.GpsRecords.Count == 0 ? null : snapshot.GpsRecords.ToArray(),
            Markers: snapshot.Markers,
            StreamGaps: snapshot.StreamGaps,
            FinalStatus: snapshot.FinalStatus,
            MissingFinalStatus: snapshot.MissingFinalStatus)
        {
            TemperatureData = snapshot.TemperatureSamples.ToArray(),
        };
    }

    private static RawImuData? BuildImuCapture(LiveCaptureContent snapshot)
    {
        if (snapshot.SessionHeader is null || snapshot.ImuSegmentsByLocation.Count == 0)
        {
            return null;
        }

        var activeLocations = snapshot.SessionHeader.GetActiveImuLocations();
        var imuData = new RawImuData
        {
            SampleRate = (int)snapshot.SessionHeader.AcceptedImuHz,
            ActiveLocations = [.. activeLocations.Select(location => (byte)location)],
            Meta =
            [
                .. activeLocations.Select(location => new ImuMetaEntry(
                    LocationId: (byte)location,
                    AccelLsbPerG: snapshot.SessionHeader.ImuCalibrationScales.GetAccelScale(location),
                    GyroLsbPerDps: snapshot.SessionHeader.ImuCalibrationScales.GetGyroScale(location)))
            ],
        };

        var capturedSegments = new List<RawImuSegment>();
        foreach (var location in activeLocations)
        {
            if (!snapshot.ImuSegmentsByLocation.TryGetValue(location, out var segments))
            {
                continue;
            }

            capturedSegments.AddRange(segments.Select(segment => new RawImuSegment
            {
                LocationId = (byte)location,
                FirstIndex = segment.FirstIndex,
                FirstMonotonicDeltaUs = segment.FirstMonotonicDeltaUs,
                Records = [.. segment.Values],
            }));
        }

        if (capturedSegments.Count == 0)
        {
            return null;
        }

        return RawImuDataSegmentHelper.FinalizeCanonicalSegments(imuData with { Segments = [.. capturedSegments] }, snapshot.StreamGaps);
    }

    private static RawCountSegment[] ToRawCountSegments(FixedRateSegment<ushort>[] segments) =>
        segments.Select(segment => new RawCountSegment
        {
            FirstIndex = segment.FirstIndex,
            FirstMonotonicDeltaUs = segment.FirstMonotonicDeltaUs,
            Counts = segment.Values.ToArray(),
        }).ToArray();
}
