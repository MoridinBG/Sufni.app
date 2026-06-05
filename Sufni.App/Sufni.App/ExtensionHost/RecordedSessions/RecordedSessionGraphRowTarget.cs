using System;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public enum RecordedSessionBuiltInGraphRow
{
    Travel,
    Velocity,
    Imu,
    PitchRoll,
    Speed,
    Elevation,
}

public readonly record struct RecordedSessionGraphRowTarget
{
    private RecordedSessionGraphRowTarget(
        RecordedSessionBuiltInGraphRow? builtInRow,
        string? extensionId,
        string? contributionId)
    {
        BuiltInRow = builtInRow;
        ExtensionId = extensionId;
        ContributionId = contributionId;
    }

    public bool IsBuiltIn => BuiltInRow.HasValue;
    public RecordedSessionBuiltInGraphRow? BuiltInRow { get; }
    public string? ExtensionId { get; }
    public string? ContributionId { get; }

    public string StableKey => IsBuiltIn
        ? $"builtin:{BuiltInStableKey(BuiltInRow!.Value)}"
        : $"extension:{ExtensionId}:{ContributionId}";

    public static RecordedSessionGraphRowTarget BuiltIn(RecordedSessionBuiltInGraphRow row) => new(row, null, null);

    public static RecordedSessionGraphRowTarget Extension(string extensionId, string contributionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            throw new ArgumentException("Extension id is required.", nameof(extensionId));
        }

        if (string.IsNullOrWhiteSpace(contributionId))
        {
            throw new ArgumentException("Contribution id is required.", nameof(contributionId));
        }

        return new RecordedSessionGraphRowTarget(null, extensionId, contributionId);
    }

    private static string BuiltInStableKey(RecordedSessionBuiltInGraphRow row)
    {
        return row switch
        {
            RecordedSessionBuiltInGraphRow.Travel => "travel",
            RecordedSessionBuiltInGraphRow.Velocity => "velocity",
            RecordedSessionBuiltInGraphRow.Imu => "imu",
            RecordedSessionBuiltInGraphRow.PitchRoll => "pitch-roll",
            RecordedSessionBuiltInGraphRow.Speed => "speed",
            RecordedSessionBuiltInGraphRow.Elevation => "elevation",
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, null),
        };
    }
}
