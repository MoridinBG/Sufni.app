using System.Collections.Immutable;

namespace Sufni.Telemetry;

/// <summary>A labelled range in telemetry seconds of one session.</summary>
public sealed record TerrainLabelRange(double StartSeconds, double EndSeconds, RoughnessClass Class);

/// <summary>
/// The labels of one session, in order of start and then of label ID. The first range that holds a window counts,
/// so labels that overlap after a sync resolve the same way on every device.
/// </summary>
public sealed record TerrainLabelSet(ImmutableArray<TerrainLabelRange> Ranges)
{
    public static TerrainLabelSet None { get; } = new(ImmutableArray<TerrainLabelRange>.Empty);

    /// <summary>
    /// The label class of a moving window whose midpoint is inside the first range that holds it. A climb class labels
    /// only a climb window and a descent class only a window that is not a climb.
    /// </summary>
    public RoughnessClass? ClassAt(TerrainWindow window)
    {
        if (!window.Moving) return null;
        var midpoint = (window.StartSeconds + window.EndSeconds) / 2;
        foreach (var range in Ranges)
        {
            if (midpoint < range.StartSeconds || midpoint >= range.EndSeconds) continue;
            return range.Class.IsClimb() == window.Climbing ? range.Class : null;
        }
        return null;
    }

    public bool Equals(TerrainLabelSet? other) => other is not null && Ranges.SequenceEqual(other.Ranges);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var range in Ranges) hash.Add(range);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The labelled readings of one ride as level distributions. A descent reading moves to the bin it would have at the
/// centre of Medium, so its distribution has the median that the label asks for. A climb reading keeps its raw frame bin.
/// A distribution is null when no labelled window adds to it.
/// </summary>
public sealed record TerrainLabelSummary(LevelDistribution? FrameImu, LevelDistribution? ForkImu, LevelDistribution? RearAxleImu,
    LevelDistribution? SmoothClimb, LevelDistribution? TechnicalClimb);

public static class TerrainLabels
{
    /// <summary>Covers the labelled window rule, the eligible reading rule and the bin shift.</summary>
    public const int Version = 1;

    public static TerrainLabelSummary Summarize(TerrainProfile profile, TerrainLabelSet labels)
    {
        var frame = new SortedDictionary<int, double>();
        var fork = new SortedDictionary<int, double>();
        var rearAxle = new SortedDictionary<int, double>();
        var smoothClimb = new SortedDictionary<int, double>();
        var technicalClimb = new SortedDictionary<int, double>();
        foreach (var window in profile.Windows)
        {
            if (labels.ClassAt(window) is not { } label) continue;
            if (label.IsClimb())
            {
                Add(label == RoughnessClass.SmoothClimb ? smoothClimb : technicalClimb, window.Frame, static r => r.Level, 0);
                continue;
            }
            var shift = ((int)label - (int)RoughnessClass.Medium) * TerrainLevels.BinsPerStep;
            Add(frame, window.Frame, static r => r.SpeedRemoved, shift);
            Add(fork, window.Fork, static r => r.SpeedRemoved, shift);
            Add(rearAxle, window.RearAxle, static r => r.SpeedRemoved, shift);
        }
        return new(Distribution(frame), Distribution(fork), Distribution(rearAxle), Distribution(smoothClimb), Distribution(technicalClimb));
    }

    private static void Add(SortedDictionary<int, double> bins, SensorRoughness? reading, Func<SensorRoughness, double> level, int shift)
    {
        if (TerrainProfileCalculator.Eligible(reading) is not { } measured) return;
        var bin = TerrainLevels.Bin(level(measured)) - shift;
        bins[bin] = bins.GetValueOrDefault(bin) + TerrainProfileCalculator.WindowMetres;
    }

    private static LevelDistribution? Distribution(SortedDictionary<int, double> bins) =>
        bins.Count == 0 ? null : new(bins.ToImmutableSortedDictionary());
}
