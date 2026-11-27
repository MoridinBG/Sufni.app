using System.Collections.Immutable;

namespace Sufni.Telemetry;

public enum SensorDecision : byte
{
    Drop = 0,
    Keep = 1,
}

public sealed record SensorDecisionEntry(SourceTimeRange Range, SensorDecision Decision);

/// <param name="Entries">Sorted by start, disjoint, and with no touching entries of the same decision.</param>
public sealed record SensorDecisionList(SensorIdentity Sensor, ImmutableArray<SensorDecisionEntry> Entries)
{
    public bool Equals(SensorDecisionList? other) =>
        other is not null && Sensor == other.Sensor && Entries.SequenceEqual(other.Entries);

    public override int GetHashCode() => HashCode.Combine(Sensor, Entries.Length);
}

/// <summary>
/// The Drop and Keep decisions the user made on sensor findings, in normalized form: one list per sensor, ordered
/// by sensor. Two values are equal when they hold the same decisions.
/// </summary>
public sealed record SensorDecisions(ImmutableArray<SensorDecisionList> Sensors)
{
    public static SensorDecisions None { get; } = new(ImmutableArray<SensorDecisionList>.Empty);

    public bool IsEmpty => Sensors.IsDefaultOrEmpty;

    /// <summary>These decisions with <paramref name="ranges"/> of <paramref name="sensor"/> decided as
    /// <paramref name="decision"/>. The new decision replaces earlier ones where they overlap.</summary>
    public SensorDecisions With(SensorIdentity sensor, SensorDecision decision, IEnumerable<SourceTimeRange> ranges)
    {
        var added = SourceTimeRanges.Union(ranges);
        var kept = Entries(sensor)
            .SelectMany(entry => SourceTimeRanges.Subtract([entry.Range], added)
                .Select(range => new SensorDecisionEntry(range, entry.Decision)));
        var entries = Normalize(kept.Concat(added.Select(range => new SensorDecisionEntry(range, decision))));
        IEnumerable<SensorDecisionList> others = IsEmpty ? [] : Sensors.Where(list => list.Sensor != sensor);
        return new([.. others.Append(new SensorDecisionList(sensor, entries)).OrderBy(list => list.Sensor.Kind)
            .ThenBy(list => list.Sensor.Location)]);
    }

    /// <summary>Only the Drop decisions.</summary>
    public SensorDecisions Drops => IsEmpty ? None : new([.. Sensors
        .Select(list => list with { Entries = [.. list.Entries.Where(entry => entry.Decision == SensorDecision.Drop)] })
        .Where(list => list.Entries.Length > 0)]);

    public IReadOnlyList<SourceTimeRange> DropRanges(SensorIdentity sensor) =>
        [.. Entries(sensor).Where(entry => entry.Decision == SensorDecision.Drop).Select(entry => entry.Range)];

    /// <summary>
    /// The parts of <paramref name="findings"/> that no decision covers. A part cut shorter than
    /// <see cref="SensorHealthParameters.MinimumFaultSeconds"/> by a decision is left out. Notices pass unchanged.
    /// </summary>
    public ImmutableArray<SensorFinding> Undecided(IEnumerable<SensorFinding> findings)
    {
        var result = ImmutableArray.CreateBuilder<SensorFinding>();
        foreach (var finding in findings)
        {
            if (finding.IsNotice)
            {
                result.Add(finding);
                continue;
            }
            var decided = Entries(finding.Sensor).Select(entry => entry.Range).ToList();
            var ranges = finding.Ranges
                .SelectMany(range => SourceTimeRanges.Subtract([range], decided)
                    .Where(part => part == range || part.DurationSeconds >= SensorHealthParameters.MinimumFaultSeconds))
                .ToImmutableArray();
            if (ranges.Length > 0) result.Add(finding with { Ranges = ranges });
        }
        return result.ToImmutable();
    }

    public bool Equals(SensorDecisions? other) =>
        other is not null && (IsEmpty ? other.IsEmpty : !other.IsEmpty && Sensors.SequenceEqual(other.Sensors));

    public override int GetHashCode() => IsEmpty ? 0 : Sensors.Length;

    private IEnumerable<SensorDecisionEntry> Entries(SensorIdentity sensor) => IsEmpty
        ? []
        : Sensors.FirstOrDefault(list => list.Sensor == sensor)?.Entries ?? [];

    private static ImmutableArray<SensorDecisionEntry> Normalize(IEnumerable<SensorDecisionEntry> entries)
    {
        var result = new List<SensorDecisionEntry>();
        foreach (var entry in entries.Where(entry => entry.Range.DurationSeconds > 0).OrderBy(entry => entry.Range.StartSeconds))
        {
            if (result.Count > 0 && result[^1].Decision == entry.Decision &&
                entry.Range.StartSeconds <= result[^1].Range.EndSeconds)
            {
                var last = result[^1];
                result[^1] = last with { Range = last.Range with { EndSeconds = Math.Max(last.Range.EndSeconds, entry.Range.EndSeconds) } };
            }
            else
            {
                result.Add(entry);
            }
        }
        return [.. result];
    }
}
