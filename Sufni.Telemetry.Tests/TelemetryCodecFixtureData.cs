namespace Sufni.Telemetry.Tests;

internal static class TelemetryCodecFixtureData
{
    public static TelemetryData FullGraph() => new()
    {
        Metadata = new Metadata { SourceName = "schema.sst", Version = 5, SampleRate = 100, Timestamp = 1700000000, Duration = 1.25 },
        Front = new Suspension
        {
            Present = true, MaxTravel = 150, AnomalyRate = 0.25,
            Travel = [10, 20, 30], Velocity = [-100, 0, 100], TravelBins = [0, 75, 150], VelocityBins = [-100, 0, 100],
            Strokes = new Strokes
            {
                Compressions = [new Stroke { Start = 0, End = 2, StartSeconds = 0.1, EndSeconds = 0.12,
                    Stat = new StrokeStat { SumTravel = 60, MaxTravel = 30, SumVelocity = 0, MaxVelocity = 100, Count = 3, Bottomouts = 1 } }],
                Rebounds = [],
            },
            Segments = [new ProcessedSuspensionSegment { FirstDenseIndex = 0, FirstSourceIndex = 10, StartSeconds = 0.1, SampleCount = 3 }],
            HasGaps = true,
        },
        Rear = new Suspension { Travel = [], Velocity = [], TravelBins = [], VelocityBins = [], Strokes = new Strokes { Compressions = [], Rebounds = [] } },
        Airtimes = [new Airtime { Start = 0.5, End = 0.75 }],
        Markers = [new MarkerData(0.4)],
        ImuData = new RawImuData
        {
            SampleRate = 100, Meta = [new ImuMetaEntry(1, 4096, 32.8f)], ActiveLocations = [1], HasGaps = true,
            Segments = [new RawImuSegment { LocationId = 1, FirstIndex = 10, FirstMonotonicDeltaUs = 100000, Records = [new ImuRecord(1, 2, 3, 4, 5, 6)] }],
        },
        GpsData = [new GpsRecord(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 47, 19, 100, 5, 90, 3, 12, 1.5f, 2)],
        TemperatureAverages = [new TemperatureAverage(1, 22.5)],
    };

    public static LiveTelemetryCapture LiveCapture()
    {
        var raw = RawTelemetryData.FromByteArray(GoldenTelemetryCompatibilityFixtures.SstV5.GetBytes());
        return new LiveTelemetryCapture(
            new Metadata { SourceName = "live", Version = 4, SampleRate = 200, Timestamp = 1700000000, Duration = 0.02 },
            new BikeData(150, 150, value => value, value => value), raw.FrontSegments, raw.RearSegments,
            raw.ImuData, raw.GpsData, [new MarkerData(0.01)], raw.StreamGaps, raw.FinalStatus, raw.MissingFinalStatus)
        {
            TemperatureData = [new TemperatureSample(1700000000, 1, 22.5f)],
        };
    }
}
