using static Sufni.Telemetry.Tests.SyntheticSensorRecording;

namespace Sufni.Telemetry.Tests;

public class SensorHealthInspectorTests
{
    private const double Duration = 60;
    private static readonly SensorIdentity Fork = new(SensorKind.ForkTravel);
    private static readonly SensorIdentity Shock = new(SensorKind.ShockTravel);
    private static readonly SensorIdentity Gps = new(SensorKind.Gps);

    private static IReadOnlyList<SensorFinding> Inspect(RawTelemetryData raw, double sourceStartSeconds = 0) =>
        SensorHealthInspector.Inspect(raw, Bike, raw.RecordingDurationSeconds!.Value, sourceStartSeconds);

    private static SourceTimeRange[] RangesOf(IReadOnlyList<SensorFinding> findings, SensorIdentity sensor, SensorFaultReason reason) =>
        [.. Assert.Single(findings, finding => finding.Sensor == sensor && finding.Reason == reason).Ranges];

    [Fact]
    public void HealthyTravel_HasNoFinding()
    {
        Assert.Empty(Inspect(Recording(Duration, Riding, Riding)));
    }

    [Fact]
    public void BothSidesQuiet_HasNoFinding()
    {
        Assert.Empty(Inspect(Recording(Duration, Still, Still)));
    }

    [Fact]
    public void TravelOnALooseArm_IsOutOfRange_FromWhereItJumps()
    {
        var findings = Inspect(Recording(Duration, Riding, During(LooseArm, Riding, (20, Duration))));

        Assert.Equal([new SourceTimeRange(20, 60)], RangesOf(findings, Shock, SensorFaultReason.OutOfRange));
        Assert.DoesNotContain(findings, finding => finding.Sensor == Fork);
    }

    [Fact]
    public void TravelThatStopsWhileTheOtherSideMoves_IsFrozen()
    {
        var findings = Inspect(Recording(Duration, Riding, During(Still, Riding, (10, 30))));

        Assert.Equal([new SourceTimeRange(10, 30)], RangesOf(findings, Shock, SensorFaultReason.Frozen));
    }

    [Fact]
    public void TravelGlitches_ThatRecover_GiveOneRangeEach()
    {
        var findings = Inspect(Recording(Duration, Riding, During(LooseArm, Riding, (10, 15), (40, 44))));

        Assert.Equal([new SourceTimeRange(10, 15), new SourceTimeRange(40, 44)],
            RangesOf(findings, Shock, SensorFaultReason.OutOfRange));
    }

    [Fact]
    public void TravelGlitches_LessThanTheMinimumApart_JoinIntoOneRange()
    {
        var findings = Inspect(Recording(Duration, Riding, During(LooseArm, Riding, (10, 15), (16, 20))));

        Assert.Equal([new SourceTimeRange(10, 20)], RangesOf(findings, Shock, SensorFaultReason.OutOfRange));
    }

    [Fact]
    public void TravelGlitch_ShorterThanTheMinimum_HasNoFinding()
    {
        Assert.Empty(Inspect(Recording(Duration, Riding, During(LooseArm, Riding, (10, 11)))));
    }

    [Fact]
    public void Ranges_AreOnTheSourceTimeline()
    {
        var findings = Inspect(Recording(Duration, Riding, During(Still, Riding, (10, 30))), sourceStartSeconds: 100);

        Assert.Equal([new SourceTimeRange(110, 130)], RangesOf(findings, Shock, SensorFaultReason.Frozen));
    }

    [Fact]
    public void ImuThatReadsZero_IsZero_EvenWhileStationary()
    {
        var raw = Recording(Duration, Still, Still);
        raw.ImuData = Imu(1, Duration, _ => (0, 0, 0));

        Assert.Equal([new SourceTimeRange(0, 60)],
            RangesOf(Inspect(raw), new SensorIdentity(SensorKind.Imu, 1), SensorFaultReason.Zero));
    }

    [Fact]
    public void FlatImu_IsFrozen_OnlyWhileMoving()
    {
        var raw = Recording(Duration, During(Riding, Still, (20, 40)), During(Riding, Still, (20, 40)));
        raw.ImuData = Imu(0, Duration, _ => (0, 0, 1));

        Assert.Equal([new SourceTimeRange(20, 40)],
            RangesOf(Inspect(raw), new SensorIdentity(SensorKind.Imu, 0), SensorFaultReason.Frozen));
    }

    [Fact]
    public void ShakenImu_HasNoFinding()
    {
        var raw = Recording(Duration, Riding, Riding);
        raw.ImuData = Imu(0, Duration, t => (0, 0, 1 + 0.3 * Math.Sin(2 * Math.PI * 7 * t)));

        Assert.Empty(Inspect(raw));
    }

    private static GpsRecord[] RideFixes(Func<int, double> offsetMetres) =>
        [.. Enumerable.Range(0, (int)Duration).Select(t => Fix(t, 7 * t + offsetMetres(t)))];

    [Fact]
    public void GpsGlitch_ThatReturnsWithinTheLimit_IsAGlitch()
    {
        var raw = Recording(Duration, Riding, Riding);
        raw.GpsData = RideFixes(t => t is >= 20 and < 20 + SensorHealthParameters.GpsGlitchMaximumFixes ? 300 : 0);

        Assert.Equal([new SourceTimeRange(20, 20 + SensorHealthParameters.GpsGlitchMaximumFixes)],
            RangesOf(Inspect(raw), Gps, SensorFaultReason.Glitch));
    }

    [Fact]
    public void GpsGlitch_ThatReturnsOneFixTooLate_HasNoFinding()
    {
        var raw = Recording(Duration, Riding, Riding);
        raw.GpsData = RideFixes(t => t is >= 20 and <= 20 + SensorHealthParameters.GpsGlitchMaximumFixes ? 300 : 0);

        Assert.Empty(Inspect(raw));
    }

    [Fact]
    public void GpsStep_ThatNeverReturns_HasNoFinding()
    {
        var raw = Recording(Duration, Riding, Riding);
        raw.GpsData = RideFixes(t => t >= 20 ? 300 : 0);

        Assert.Empty(Inspect(raw));
    }

    [Fact]
    public void GpsGap_WhileMoving_IsAFixLostNotice()
    {
        var raw = Recording(Duration, Riding, Riding);
        raw.GpsData = [.. RideFixes(_ => 0).Where((_, t) => t is < 20 or >= 30)];

        var finding = Assert.Single(Inspect(raw));

        Assert.Equal((Gps, SensorFaultReason.FixLost), (finding.Sensor, finding.Reason));
        Assert.Equal([new SourceTimeRange(19, 30)], finding.Ranges);
        Assert.True(finding.IsNotice);
    }

    [Fact]
    public void GpsGap_WhileStationary_HasNoFinding()
    {
        var raw = Recording(Duration, Still, Still);
        raw.GpsData = [.. Enumerable.Range(0, (int)Duration).Where(t => t is < 20 or >= 30).Select(t => Fix(t, 0))];

        Assert.Empty(Inspect(raw));
    }

    private static TemperatureSample[] Temperatures(double duration, Func<long, float> celsius) =>
        [.. Enumerable.Range(0, (int)(duration / 30)).Select(i => Temperature(i * 30L, celsius(i * 30L)))];

    [Fact]
    public void TemperatureThatJumpsAndSticks_IsJumpAndStuck_UntilItChanges()
    {
        var raw = Recording(600, Riding, Riding);
        raw.TemperatureData = Temperatures(600, t => t is >= 120 and < 300 ? -40 : 20 + t / 60f);

        Assert.Equal([new SourceTimeRange(120, 300)],
            RangesOf(Inspect(raw), new SensorIdentity(SensorKind.Temperature, 0), SensorFaultReason.JumpAndStuck));
    }

    [Fact]
    public void StableTemperature_HasNoFinding()
    {
        var raw = Recording(600, Riding, Riding);
        raw.TemperatureData = Temperatures(600, _ => 21);

        Assert.Empty(Inspect(raw));
    }

    [Fact]
    public void TemperatureWithMissingSamples_IsANoValueNotice()
    {
        var raw = Recording(600, Riding, Riding);
        raw.TemperatureData = [.. Temperatures(600, _ => 21).Where(sample =>
            sample.TimestampUtc - StartUnixSeconds is < 120 or > 300)];

        Assert.Equal([new SourceTimeRange(90, 330)],
            RangesOf(Inspect(raw), new SensorIdentity(SensorKind.Temperature, 0), SensorFaultReason.NoValue));
    }
}
