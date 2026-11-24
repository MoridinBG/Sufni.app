using System.Collections.Immutable;

namespace Sufni.Telemetry;

/// <summary>A union of half-open intervals on the telemetry clock.</summary>
public sealed record TelemetryTimeSelection
{
    public ImmutableArray<TelemetryTimeRange> Ranges { get; }

    public TelemetryTimeSelection(IEnumerable<TelemetryTimeRange> ranges)
    {
        var merged = ImmutableArray.CreateBuilder<TelemetryTimeRange>();
        foreach (var range in ranges.OrderBy(r => r.StartSeconds))
        {
            if (merged.Count == 0 || merged[^1].EndSeconds < range.StartSeconds) merged.Add(range);
            else if (range.EndSeconds > merged[^1].EndSeconds)
                merged[^1] = new(merged[^1].StartSeconds, range.EndSeconds);
        }
        Ranges = merged.ToImmutable();
    }

    public static implicit operator TelemetryTimeSelection?(TelemetryTimeRange? range) =>
        range is { } value ? new([value]) : null;

    public bool Contains(double seconds) => CoversSamples(seconds, seconds);

    internal bool CoversSamples(double first, double last)
    {
        var lo = 0;
        var hi = Ranges.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (Ranges[mid].EndSeconds <= first) lo = mid + 1;
            else hi = mid;
        }
        return lo < Ranges.Length && Ranges[lo].StartSeconds <= first && last < Ranges[lo].EndSeconds;
    }

    public bool Equals(TelemetryTimeSelection? other) => other is not null && Ranges.AsSpan().SequenceEqual(other.Ranges.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var range in Ranges) hash.Add(range);
        return hash.ToHashCode();
    }
}

internal readonly record struct SuspensionSampleInterval(int Start, int EndExclusive)
{
    public int Count => EndExclusive - Start;

    public static List<SuspensionSampleInterval> Select(IReadOnlyList<ProcessedSuspensionSegment> segments,
        int sampleCount, int sampleRate, TelemetryTimeSelection? selection)
    {
        var intervals = new List<SuspensionSampleInterval>();
        if (segments.Count == 0) AddSegment(0, 0, sampleCount);
        else foreach (var segment in segments)
            AddSegment(segment.FirstDenseIndex, segment.StartSeconds, segment.SampleCount);
        return intervals;

        void AddSegment(int first, double origin, int count)
        {
            if (first < 0 || first >= sampleCount || !double.IsFinite(origin)) return;
            count = Math.Min(count, sampleCount - first);
            if (count <= 0) return;
            if (selection is null) { intervals.Add(new(first, first + count)); return; }
            if (sampleRate <= 0) return;
            foreach (var range in selection.Ranges)
            {
                if (range.StartSeconds >= origin + (double)count / sampleRate) break;
                if (range.EndSeconds <= origin) continue;
                var start = LowerBound(range.StartSeconds);
                var end = LowerBound(range.EndSeconds);
                if (end > start) intervals.Add(new(first + start, first + end));
            }

            int LowerBound(double target)
            {
                var lo = 0;
                var hi = count;
                while (lo < hi)
                {
                    var mid = lo + (hi - lo) / 2;
                    if (origin + (double)mid / sampleRate < target) lo = mid + 1;
                    else hi = mid;
                }
                return lo;
            }
        }
    }
}
