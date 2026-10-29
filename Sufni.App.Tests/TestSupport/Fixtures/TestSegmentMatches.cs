using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Fixtures;

/// <summary>
/// Segment matches and the matched telemetry they compare against: 31 front
/// samples at 1 Hz with travel equal to the second and one airtime from 20.5 s to 21.5 s.
/// </summary>
internal static class TestSegmentMatches
{
    public static SessionContentToken Token { get; } = new(1, 1, null, null, 1000, null, 0);

    /// <summary>A match whose source and matched seconds are equal.</summary>
    public static SegmentMatch Match(double startSeconds, double endSeconds, Guid? sessionId = null) =>
        Match(startSeconds, endSeconds, [new(startSeconds, startSeconds), new(endSeconds, endSeconds)], sessionId);

    public static SegmentMatch Match(
        double startSeconds,
        double endSeconds,
        IReadOnlyList<SegmentTimeMapPoint> timeMap,
        Guid? sessionId = null,
        string sessionName = "Matched session",
        double normalizedCostMeters = 1) => new(
        sessionId ?? Guid.NewGuid(),
        sessionName,
        SessionTimestamp: 1000,
        StartSeconds: startSeconds,
        EndSeconds: endSeconds,
        NormalizedCostMeters: normalizedCostMeters,
        MedianErrorMeters: 1,
        P90ErrorMeters: 1,
        SourcePointCount: timeMap.Count,
        CandidatePointCount: timeMap.Count,
        TimeMap: timeMap,
        ContentToken: Token);

    public static TelemetryData Telemetry()
    {
        var travel = Enumerable.Range(0, 31).Select(value => (double)value).ToArray();
        var velocity = Enumerable.Range(0, 31).Select(value => value * 1000.0).ToArray();
        return new TelemetryData
        {
            Metadata = new Metadata
            {
                SampleRate = 1,
                Timestamp = 1000,
                Duration = 30,
            },
            Front = new Suspension
            {
                Present = true,
                MaxTravel = 100,
                Travel = travel,
                Velocity = velocity,
                Strokes = new Strokes { Compressions = [], Rebounds = [] },
                TravelBins = [],
                VelocityBins = [],
            },
            Rear = new Suspension
            {
                Present = false,
                MaxTravel = null,
                Travel = [],
                Velocity = [],
                Strokes = new Strokes { Compressions = [], Rebounds = [] },
                TravelBins = [],
                VelocityBins = [],
            },
            Airtimes = [new Airtime { Start = 20.5, End = 21.5 }],
            Markers = [],
        };
    }
}
