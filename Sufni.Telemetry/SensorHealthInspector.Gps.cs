namespace Sufni.Telemetry;

public static partial class SensorHealthInspector
{
    private const double EarthRadiusMetres = 6_371_000;

    private static void InspectGps(RawTelemetryData raw, bool[] moving, List<SensorFinding> findings)
    {
        if (raw.GpsData is not { Length: > 0 } records) return;
        var baseUtcMs = TelemetrySlicer.RawBaseUtcMs(raw);
        var fixes = records
            .Select(record => (Seconds: (new DateTimeOffset(record.Timestamp).ToUnixTimeMilliseconds() - baseUtcMs) / 1000.0, Record: record))
            .OrderBy(fix => fix.Seconds)
            .ToArray();
        var sensor = new SensorIdentity(SensorKind.Gps);

        var glitches = new List<SourceTimeRange>();
        var accepted = 0;
        for (var i = 1; i < fixes.Length; i++)
        {
            if (Consistent(fixes[accepted], fixes[i]))
            {
                accepted = i;
                continue;
            }
            var last = Math.Min(fixes.Length - 1, i + SensorHealthParameters.GpsGlitchMaximumFixes);
            var back = -1;
            for (var j = i + 1; j <= last && back < 0; j++)
                if (Consistent(fixes[accepted], fixes[j])) back = j;
            if (back < 0)
            {
                accepted = i;
                continue;
            }
            glitches.Add(new SourceTimeRange(fixes[i].Seconds, fixes[back].Seconds));
            accepted = back;
            i = back;
        }
        Add(findings, sensor, SensorFaultReason.Glitch, glitches, dropShort: false);

        var lost = new List<SourceTimeRange>();
        for (var i = 1; i < fixes.Length; i++)
        {
            var (start, end) = (fixes[i - 1].Seconds, fixes[i].Seconds);
            if (end - start <= TrackMotion.GapSeconds) continue;
            var first = Math.Max(0, (int)Math.Floor(start));
            var stop = Math.Min(moving.Length, (int)Math.Ceiling(end));
            for (var k = first; k < stop; k++)
            {
                if (!moving[k]) continue;
                lost.Add(new SourceTimeRange(start, end));
                break;
            }
        }
        Add(findings, sensor, SensorFaultReason.FixLost, lost);
    }

    private static bool Consistent((double Seconds, GpsRecord Record) from, (double Seconds, GpsRecord Record) to)
    {
        var elapsed = to.Seconds - from.Seconds;
        return elapsed <= 0 ||
            Distance(from.Record, to.Record) / elapsed <= SensorHealthParameters.GpsMaximumPlausibleSpeedMetresPerSecond;
    }

    private static double Distance(GpsRecord a, GpsRecord b)
    {
        const double radians = Math.PI / 180;
        var x = (b.Longitude - a.Longitude) * radians * Math.Cos((a.Latitude + b.Latitude) / 2 * radians);
        var y = (b.Latitude - a.Latitude) * radians;
        return Math.Sqrt(x * x + y * y) * EarthRadiusMetres;
    }
}
