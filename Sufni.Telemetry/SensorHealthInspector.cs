using System.Collections.Immutable;

namespace Sufni.Telemetry;

/// <summary>
/// Finds wrong and missing readings of each sensor in a recording. The rules and their thresholds are in
/// <see cref="SensorHealthParameters"/>.
/// </summary>
public static partial class SensorHealthInspector
{
    /// <param name="raw">The recording, after the derivation window slice and before any drop.</param>
    /// <param name="durationSeconds">The duration of <paramref name="raw"/>.</param>
    /// <param name="sourceStartSeconds">Where <paramref name="raw"/> starts on the recording source timeline.</param>
    public static ImmutableArray<SensorFinding> Inspect(RawTelemetryData raw, BikeData bike, double durationSeconds,
        double sourceStartSeconds)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(bike);
        var seconds = Math.Max(0, (int)Math.Ceiling(durationSeconds));
        var findings = new List<SensorFinding>();
        var moving = InspectTravel(raw, bike, seconds, findings);
        InspectImu(raw, moving, findings);
        InspectGps(raw, moving, findings);
        InspectTemperature(raw, durationSeconds, findings);
        return [.. findings
            .Select(finding => finding with
            {
                Ranges = [.. finding.Ranges.Select(range =>
                    new SourceTimeRange(range.StartSeconds + sourceStartSeconds, range.EndSeconds + sourceStartSeconds))],
            })
            .OrderBy(finding => finding.Sensor.Kind).ThenBy(finding => finding.Sensor.Location).ThenBy(finding => finding.Reason)];
    }

    private static void Add(List<SensorFinding> findings, SensorIdentity sensor, SensorFaultReason reason,
        IEnumerable<SourceTimeRange> ranges, bool dropShort = true)
    {
        var merged = SourceTimeRanges.Union(ranges, SensorHealthParameters.MinimumFaultSeconds)
            .Where(range => !dropShort || range.DurationSeconds >= SensorHealthParameters.MinimumFaultSeconds)
            .ToImmutableArray();
        if (merged.Length > 0) findings.Add(new SensorFinding(sensor, reason, merged));
    }

    /// <summary>One range for each run of bad seconds.</summary>
    private static IEnumerable<SourceTimeRange> SecondRanges(bool[] bad)
    {
        for (var start = 0; start < bad.Length; start++)
        {
            if (!bad[start]) continue;
            var end = start;
            while (end + 1 < bad.Length && bad[end + 1]) end++;
            yield return new SourceTimeRange(start, end + 1);
            start = end;
        }
    }

    private static int SecondOf(double seconds, int count) => seconds >= 0 && seconds < count ? (int)seconds : -1;
}
