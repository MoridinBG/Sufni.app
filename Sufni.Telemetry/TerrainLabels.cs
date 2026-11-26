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
