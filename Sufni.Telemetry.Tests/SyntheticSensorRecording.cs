namespace Sufni.Telemetry.Tests;

/// <summary>Raw recordings built from count, IMU, GPS and temperature signals over time.</summary>
internal static class SyntheticSensorRecording
{
    public const int Rate = 100;
    public const ushort MaxTravelCount = 1000;
    public const double MaxTravel = 150;
    public const long StartUnixSeconds = 1_750_000_000;
    private const double MetresPerDegree = 111_195;

    /// <summary>A rotational fork and shock whose travel reaches its maximum at <see cref="MaxTravelCount"/>.</summary>
    public static BikeData Bike { get; } = new(MaxTravel, MaxTravel, CountToTravel, CountToTravel, true, true,
        MaxTravelCount, MaxTravelCount);

    private static double CountToTravel(ushort count) => Math.Clamp(count % 4096 * MaxTravel / MaxTravelCount, 0, MaxTravel);

    /// <summary>A ridden side: 2 Hz strokes between counts 100 and 500.</summary>
    public static double Riding(double seconds) => 300 + 200 * Math.Sin(2 * Math.PI * 2 * seconds);

    public static double Still(double seconds) => 50;

    /// <summary>A loose sensor arm: the reading sits near -790 and swings through count 0 at each peak.</summary>
    public static double LooseArm(double seconds) => -790 + 900 * Math.Sin(2 * Math.PI * 1.5 * seconds);

    /// <summary>A signal that follows <paramref name="inside"/> within any range and <paramref name="outside"/>
    /// elsewhere.</summary>
    public static Func<double, double> During(Func<double, double> inside, Func<double, double> outside,
        params (double Start, double End)[] ranges) =>
        seconds => ranges.Any(range => seconds >= range.Start && seconds < range.End) ? inside(seconds) : outside(seconds);

    public static ushort[] Counts(Func<double, double> signal, double durationSeconds) =>
        [.. Enumerable.Range(0, (int)Math.Round(durationSeconds * Rate))
            .Select(i => (ushort)(((int)Math.Round(signal(i / (double)Rate)) % 4096 + 4096) % 4096))];

    public static RawTelemetryData Recording(double durationSeconds, Func<double, double>? front = null,
        Func<double, double>? rear = null) => new()
    {
        Version = 4,
        SampleRate = Rate,
        Timestamp = StartUnixSeconds,
        SessionStartUtcMs = StartUnixSeconds * 1000,
        RecordingDurationSeconds = durationSeconds,
        Front = front is null ? [] : Counts(front, durationSeconds),
        Rear = rear is null ? [] : Counts(rear, durationSeconds),
    };

    /// <summary>One IMU at <paramref name="location"/> with 4096 LSB per g, reading
    /// <paramref name="acceleration"/> in g.</summary>
    public static RawImuData Imu(byte location, double durationSeconds, Func<double, (double X, double Y, double Z)> acceleration) => new()
    {
        SampleRate = Rate,
        Meta = [new ImuMetaEntry(location, 4096, 16)],
        ActiveLocations = [location],
        Records = [.. Enumerable.Range(0, (int)Math.Round(durationSeconds * Rate)).Select(i =>
        {
            var (x, y, z) = acceleration(i / (double)Rate);
            return new ImuRecord((short)Math.Round(x * 4096), (short)Math.Round(y * 4096), (short)Math.Round(z * 4096), 0, 0, 0);
        })],
    };

    /// <summary>A fix at <paramref name="seconds"/> into the recording, <paramref name="eastMetres"/> east of the
    /// start.</summary>
    public static GpsRecord Fix(double seconds, double eastMetres) => new(
        DateTimeOffset.FromUnixTimeMilliseconds(StartUnixSeconds * 1000 + (long)Math.Round(seconds * 1000)).UtcDateTime,
        0, eastMetres / MetresPerDegree, 0, 0, 0, 3, 12, 2, 3);

    /// <summary>Derives <paramref name="raw"/>, starting at <paramref name="sourceStartSeconds"/> on its source
    /// timeline, with <paramref name="drops"/> decided as Drop.</summary>
    public static TelemetryData Derive(RawTelemetryData raw, double sourceStartSeconds,
        params (SensorIdentity Sensor, double Start, double End)[] drops)
    {
        var decisions = drops.Aggregate(SensorDecisions.None, (current, drop) =>
            current.With(drop.Sensor, SensorDecision.Drop, [new SourceTimeRange(drop.Start, drop.End)]));
        var metadata = new Metadata { SourceName = "synthetic", Version = 4, SampleRate = Rate, Duration = raw.RecordingDurationSeconds!.Value };
        return TelemetryData.FromRecording(raw, metadata, Bike, new TelemetryProcessingOptions { Drops = decisions }, sourceStartSeconds);
    }

    public static TemperatureSample Temperature(long seconds, float celsius, byte location = 0) =>
        new(StartUnixSeconds + seconds, location, celsius);
}
