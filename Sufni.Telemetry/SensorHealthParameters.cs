namespace Sufni.Telemetry;

/// <summary>Thresholds of the sensor fault detection.</summary>
public static class SensorHealthParameters
{
    // (s) shortest finding range, and the largest gap that joins two ranges. Spike elimination already removes
    // shorter spikes.
    public const double MinimumFaultSeconds = 2;

    // (counts) distance of a per-second median from the reachable count range before it is out of range. The
    // lowest healthy median is -13; a loose rear sensor arm settles near -790.
    public const int TravelRangeToleranceCounts = 64;

    // (reachable spans per second) activity at which a travel side counts as active, and the bike as moving. A
    // ridden fork measures 1.6-5.3; a standing bike about 0.
    public const double TravelActiveMinimumSpanFractionPerSecond = 1.0;

    // (ratio) activity of a side, divided by the activity of the active other side, below which the side is
    // frozen. Healthy rides give no range; broken rear sensors give 10-251 s.
    public const double TravelFrozenMaximumActivityRatio = 0.15;

    // (g) standard deviation of the acceleration magnitude within a moving second below which an IMU is frozen.
    // The healthy minimum while moving is 0.015 g.
    public const double ImuFrozenMaximumStdDevG = 0.005;

    // (m/s) highest speed implied by two consecutive GPS fixes that is still plausible. The healthy maximum is
    // 15.5 m/s.
    public const double GpsMaximumPlausibleSpeedMetresPerSecond = 30;

    // (fixes) longest run of implausible fixes that ends as a glitch. A longer run is a step of the position.
    public const int GpsGlitchMaximumFixes = 4;

    // (°C) smallest step between consecutive samples that can start a stuck temperature. The largest healthy
    // step is 6.2 °C.
    public const double TemperatureJumpMinimumCelsius = 10;

    // (sample intervals) equal samples after a jump that make it stuck, and the longest time without a sample
    // before a value is missing. Three intervals are 90 s at the 30 s cadence.
    public const int TemperatureMinimumSamples = 3;
}
