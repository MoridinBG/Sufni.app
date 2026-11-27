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
    public RoughnessClass? ClassAt(TerrainWindow window) => RangeAt(window)?.Class;

    /// <summary>The range whose class labels the window, under the rule of <see cref="ClassAt"/>.</summary>
    public TerrainLabelRange? RangeAt(TerrainWindow window)
    {
        if (!window.Moving) return null;
        var midpoint = (window.StartSeconds + window.EndSeconds) / 2;
        foreach (var range in Ranges)
        {
            if (midpoint < range.StartSeconds || midpoint >= range.EndSeconds) continue;
            return range.Class.IsClimb() == window.Climbing ? range : null;
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

/// <summary>The typical level of one sensor over the eligible readings of a labelled stretch, and their distance.</summary>
public sealed record StretchLevel(double Level, double Metres);

/// <summary>
/// A label and the typical levels of its stretch: speed-removed levels for a descent class, the raw frame level for a
/// climb class.
/// </summary>
public sealed record LabelledStretch(TerrainLabelRange Label, StretchLevel? FrameImu, StretchLevel? ForkImu, StretchLevel? RearAxleImu);

/// <summary>The labelled stretches of one ride with an eligible reading, in label order.</summary>
public sealed record TerrainLabelSummary(ImmutableArray<LabelledStretch> Stretches)
{
    public bool Equals(TerrainLabelSummary? other) => other is not null && Stretches.SequenceEqual(other.Stretches);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var stretch in Stretches) hash.Add(stretch);
        return hash.ToHashCode();
    }
}

public static class TerrainLabels
{
    /// <summary>Covers the labelled window rule, the eligible reading rule and the stretch level.</summary>
    public const int Version = 2;

    /// <summary>
    /// The class steps by which a stretch reads outside the class of its label before it clashes with the label. The
    /// typical level of 100 m varies by about 0.26 class between runs, so a whole class is not noise.
    /// </summary>
    public const double ClashSteps = 1;

    // Four class steps away from every stretch's own bin, each stretch reads Smooth or Very rough on any farther bin.
    private const int Reach = 4 * TerrainLevels.BinsPerStep;

    public static TerrainLabelSummary Summarize(TerrainProfile profile, TerrainLabelSet labels)
    {
        var readings = new Dictionary<TerrainLabelRange, (List<double> Frame, List<double> Fork, List<double> RearAxle)>();
        foreach (var window in profile.Windows)
        {
            if (labels.RangeAt(window) is not { } range) continue;
            if (!readings.TryGetValue(range, out var levels)) readings[range] = levels = ([], [], []);
            var climb = range.Class.IsClimb();
            Add(levels.Frame, window.Frame, climb);
            if (climb) continue;
            Add(levels.Fork, window.Fork, false);
            Add(levels.RearAxle, window.RearAxle, false);
        }
        return new([.. labels.Ranges.Distinct().Where(readings.ContainsKey)
            .Select(range => new LabelledStretch(range, Stretch(readings[range].Frame), Stretch(readings[range].Fork),
                Stretch(readings[range].RearAxle)))
            .Where(stretch => stretch.FrameImu is not null || stretch.ForkImu is not null || stretch.RearAxleImu is not null)]);
    }

    /// <summary>
    /// The median bin that puts the most labelled stretches inside the class of their label. Each stretch counts once,
    /// whatever its length. Of equal choices, the bin nearest the median of the bins that each stretch alone would set wins.
    /// </summary>
    public static int Anchor(IReadOnlyCollection<(double Level, RoughnessClass Class)> stretches)
    {
        var own = stretches.Select(s =>
            TerrainLevels.Bin(s.Level) - ((int)s.Class - (int)RoughnessClass.Medium) * TerrainLevels.BinsPerStep).ToArray();
        return Fit(own.Min() - Reach, own.Max() + Reach, Median(own),
            bin => stretches.Count(s => TerrainLevels.Classify(TerrainLevels.Value(s.Level, bin)) == s.Class));
    }

    /// <summary>
    /// The climb median bin, below whose level a climb is smooth, that puts the most labelled climb stretches in the class
    /// of their label. Of equal choices, the bin nearest midway between the median bins of the two classes wins.
    /// </summary>
    public static int ClimbAnchor(IReadOnlyCollection<double> smooth, IReadOnlyCollection<double> technical)
    {
        var all = smooth.Concat(technical).Select(TerrainLevels.Bin).ToArray();
        var point = (int)Math.Floor((Median([.. smooth.Select(TerrainLevels.Bin)]) + Median([.. technical.Select(TerrainLevels.Bin)])) / 2.0);
        return Fit(all.Min() - 1, all.Max() + 1, point, bin =>
            smooth.Count(level => level < TerrainLevels.Median(bin)) + technical.Count(level => level >= TerrainLevels.Median(bin)));
    }

    private static int Fit(int from, int to, int point, Func<int, int> agreement)
    {
        var (best, bestAgreement) = (point, -1);
        for (var bin = from; bin <= to; bin++)
        {
            var agrees = agreement(bin);
            if (agrees > bestAgreement || agrees == bestAgreement && Math.Abs(bin - point) < Math.Abs(best - point))
                (best, bestAgreement) = (bin, agrees);
        }
        return best;
    }

    private static int Median(int[] bins)
    {
        Array.Sort(bins);
        var middle = bins.Length / 2;
        return bins.Length % 2 == 1 ? bins[middle] : (int)Math.Floor((bins[middle - 1] + bins[middle]) / 2.0);
    }

    private static void Add(List<double> levels, SensorRoughness? reading, bool climb)
    {
        if (TerrainProfileCalculator.Eligible(reading) is { } measured) levels.Add(climb ? measured.Level : measured.SpeedRemoved);
    }

    private static StretchLevel? Stretch(List<double> levels)
    {
        if (levels.Count == 0) return null;
        levels.Sort();
        var middle = levels.Count / 2;
        // Levels compare as ratios, so the middle of an even count is the geometric mean of the two middle levels.
        var level = levels.Count % 2 == 1 ? levels[middle] : Math.Sqrt(levels[middle - 1] * levels[middle]);
        return new(level, levels.Count * TerrainProfileCalculator.WindowMetres);
    }
}
