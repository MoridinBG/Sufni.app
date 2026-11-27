namespace Sufni.Telemetry;

internal static class SourceTimeRanges
{
    /// <summary>Sorted ranges where ranges that overlap, touch, or lie less than <paramref name="joinGapSeconds"/>
    /// apart become one.</summary>
    public static List<SourceTimeRange> Union(IEnumerable<SourceTimeRange> ranges, double joinGapSeconds = 0)
    {
        var result = new List<SourceTimeRange>();
        foreach (var range in ranges.Where(range => range.EndSeconds > range.StartSeconds).OrderBy(range => range.StartSeconds))
        {
            if (result.Count > 0 && range.StartSeconds - result[^1].EndSeconds is var gap && (gap <= 0 || gap < joinGapSeconds))
            {
                var last = result[^1];
                result[^1] = last with { EndSeconds = Math.Max(last.EndSeconds, range.EndSeconds) };
            }
            else
            {
                result.Add(range);
            }
        }
        return result;
    }

    /// <summary>The parts of <paramref name="ranges"/> outside every range of <paramref name="removed"/>.</summary>
    public static List<SourceTimeRange> Subtract(IEnumerable<SourceTimeRange> ranges, IEnumerable<SourceTimeRange> removed)
    {
        var cuts = Union(removed);
        var result = new List<SourceTimeRange>();
        foreach (var range in ranges)
        {
            var start = range.StartSeconds;
            foreach (var cut in cuts)
            {
                if (cut.EndSeconds <= start) continue;
                if (cut.StartSeconds >= range.EndSeconds) break;
                if (cut.StartSeconds > start) result.Add(new(start, cut.StartSeconds));
                start = cut.EndSeconds;
            }
            if (start < range.EndSeconds) result.Add(new(start, range.EndSeconds));
        }
        return result;
    }

    public static bool Contains(IReadOnlyList<SourceTimeRange> ranges, double seconds)
    {
        foreach (var range in ranges)
            if (seconds >= range.StartSeconds && seconds < range.EndSeconds) return true;
        return false;
    }
}
