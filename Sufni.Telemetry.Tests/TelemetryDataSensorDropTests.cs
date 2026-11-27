using static Sufni.Telemetry.Tests.SyntheticSensorRecording;

namespace Sufni.Telemetry.Tests;

public class TelemetryDataSensorDropTests
{
    private const double Duration = 60;
    private const double SourceStart = 100;
    private static readonly SensorIdentity Shock = new(SensorKind.ShockTravel);

    private static TelemetryData Derive(RawTelemetryData raw, params (SensorIdentity Sensor, double Start, double End)[] drops) =>
        SyntheticSensorRecording.Derive(raw, SourceStart, drops);

    [Fact]
    public void DroppedTravel_LeavesAGapInThatSideOnly()
    {
        var telemetry = Derive(Recording(Duration, Riding, Riding), (Shock, 120, 130));

        Assert.Equal([0d, 30d], telemetry.Rear.Segments.Select(segment => segment.StartSeconds));
        Assert.True(telemetry.Rear.HasGaps);
        Assert.False(telemetry.Front.HasGaps);
    }

    [Fact]
    public void DroppedRearTravel_LeavesTheFrontUnchanged()
    {
        var raw = Recording(Duration, Riding, Riding);

        var kept = Derive(raw);
        var dropped = Derive(raw, (Shock, 120, 130));

        Assert.Equal(kept.Front.Travel, dropped.Front.Travel);
        Assert.Equal(kept.Front.Velocity, dropped.Front.Velocity);
        Assert.Equal(kept.Front.Strokes.Compressions, dropped.Front.Strokes.Compressions);
    }

    [Fact]
    public void DroppedTravel_StillHasItsFinding()
    {
        var telemetry = Derive(Recording(Duration, Riding, During(LooseArm, Riding, (20, 30))), (Shock, 120, 130));

        var finding = Assert.Single(telemetry.SensorFindings);
        Assert.Equal([new SourceTimeRange(120, 130)], finding.Ranges);
    }

    // A front jump at 10.1-10.7 s: the fork tops out, then lands fast. The rear keeps riding at mid travel.
    private static double JumpingFront(double t) => t switch
    {
        < 10 or >= 11 => Riding(t),
        < 10.1 => 300 * (10.1 - t) / 0.1,
        < 10.7 => 0,
        < 10.8 => 600 * (t - 10.7) / 0.1,
        _ => 600 - 300 * (t - 10.8) / 0.2,
    };

    [Fact]
    public void Airtime_UsesTheFrontOnlyRule_WhereTheRearIsDropped()
    {
        var raw = Recording(20, JumpingFront, Riding);

        Assert.Empty(Derive(raw).Airtimes);
        var airtime = Assert.Single(Derive(raw, (Shock, 108, 114)).Airtimes);
        Assert.InRange(airtime.Start, 10, 10.2);
        Assert.InRange(airtime.End, 10.6, 10.8);
    }

    [Fact]
    public void DroppedImu_LosesItsSamplesInTheRange()
    {
        var raw = Recording(Duration, Riding, Riding);
        raw.ImuData = Imu(0, Duration, t => (0, 0, 1 + 0.3 * Math.Sin(2 * Math.PI * 7 * t)));

        var telemetry = Derive(raw, (new SensorIdentity(SensorKind.Imu, 0), 120, 130));

        var segments = telemetry.ImuData!.SampleSegments;
        Assert.Equal(2, segments.Count);
        Assert.Equal(30_000_000ul, segments[1].FirstMonotonicDeltaUs);
        Assert.Equal((int)(Duration - 10) * Rate, segments[0].Count + segments[1].Count);
    }

    [Fact]
    public void DroppedGps_LosesItsFixesInTheRange()
    {
        var raw = Recording(Duration, Riding, Riding);
        raw.GpsData = [.. Enumerable.Range(0, (int)Duration).Select(t => Fix(t, 7 * t))];

        var telemetry = Derive(raw, (new SensorIdentity(SensorKind.Gps), 120, 130));

        Assert.Equal((int)Duration - 10, telemetry.GpsData!.Value.Length);
    }

    [Fact]
    public void DroppedTemperature_LeavesTheAverageOfTheRest()
    {
        var raw = Recording(600, Riding, Riding);
        raw.TemperatureData = [.. Enumerable.Range(0, 20).Select(i => Temperature(i * 30L, i is >= 4 and < 10 ? -40 : 20))];

        var telemetry = Derive(raw, (new SensorIdentity(SensorKind.Temperature, 0), 220, 400));

        Assert.Equal(20, Assert.Single(telemetry.TemperatureAverages).TemperatureCelsius);
    }
}
