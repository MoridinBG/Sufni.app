namespace Sufni.Telemetry;

public static partial class SensorHealthInspector
{
    private static void InspectTemperature(RawTelemetryData raw, double durationSeconds, List<SensorFinding> findings)
    {
        var baseSeconds = TelemetrySlicer.RawBaseUtcSeconds(raw);
        foreach (var location in raw.TemperatureData.GroupBy(sample => sample.LocationId).OrderBy(group => group.Key))
        {
            var samples = location
                .Select(sample => (Seconds: sample.TimestampUtc - baseSeconds, Celsius: sample.TemperatureCelsius))
                .OrderBy(sample => sample.Seconds)
                .ToArray();
            if (samples.Length < 2) continue;
            var sensor = new SensorIdentity(SensorKind.Temperature, location.Key);

            var stuck = new List<SourceTimeRange>();
            for (var i = 1; i < samples.Length; i++)
            {
                if (Math.Abs(samples[i].Celsius - samples[i - 1].Celsius) < SensorHealthParameters.TemperatureJumpMinimumCelsius) continue;
                var differs = i + 1;
                while (differs < samples.Length && samples[differs].Celsius == samples[i].Celsius) differs++;
                if (differs - i - 1 < SensorHealthParameters.TemperatureMinimumSamples) continue;
                stuck.Add(new SourceTimeRange(samples[i].Seconds, differs < samples.Length ? samples[differs].Seconds : durationSeconds));
                i = differs - 1;
            }
            Add(findings, sensor, SensorFaultReason.JumpAndStuck, stuck);

            var longest = SensorHealthParameters.TemperatureMinimumSamples * MedianInterval(samples.Select(sample => sample.Seconds).ToArray());
            var missing = new List<SourceTimeRange>();
            if (samples[0].Seconds > longest) missing.Add(new SourceTimeRange(0, samples[0].Seconds));
            for (var i = 1; i < samples.Length; i++)
                if (samples[i].Seconds - samples[i - 1].Seconds > longest)
                    missing.Add(new SourceTimeRange(samples[i - 1].Seconds, samples[i].Seconds));
            if (durationSeconds - samples[^1].Seconds > longest)
                missing.Add(new SourceTimeRange(samples[^1].Seconds, durationSeconds));
            Add(findings, sensor, SensorFaultReason.NoValue, missing);
        }
    }

    private static double MedianInterval(double[] seconds)
    {
        var intervals = new double[seconds.Length - 1];
        for (var i = 1; i < seconds.Length; i++) intervals[i - 1] = seconds[i] - seconds[i - 1];
        Array.Sort(intervals);
        var middle = intervals.Length / 2;
        return intervals.Length % 2 == 1 ? intervals[middle] : (intervals[middle - 1] + intervals[middle]) / 2;
    }
}
