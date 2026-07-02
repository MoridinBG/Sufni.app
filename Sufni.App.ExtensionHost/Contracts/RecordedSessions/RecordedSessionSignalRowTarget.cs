using System;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public enum RecordedSessionBuiltInSignalRow
{
    Travel,
    Velocity,
    Imu,
    PitchRoll,
    Speed,
    Elevation,
}

public readonly record struct RecordedSessionSignalRowTarget
{
    private RecordedSessionSignalRowTarget(
        RecordedSessionBuiltInSignalRow? builtInRow,
        string? extensionId,
        string? contributionId)
    {
        BuiltInRow = builtInRow;
        ExtensionId = extensionId;
        ContributionId = contributionId;
    }

    public bool IsBuiltIn => BuiltInRow.HasValue;
    public RecordedSessionBuiltInSignalRow? BuiltInRow { get; }
    public string? ExtensionId { get; }
    public string? ContributionId { get; }

    public string StableKey => IsBuiltIn
        ? $"builtin:{BuiltInStableKey(BuiltInRow!.Value)}"
        : $"extension:{ExtensionId}:{ContributionId}";

    public static RecordedSessionSignalRowTarget BuiltIn(RecordedSessionBuiltInSignalRow row) => new(row, null, null);

    public static RecordedSessionSignalRowTarget Extension(string extensionId, string contributionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            throw new ArgumentException("Extension id is required.", nameof(extensionId));
        }

        if (string.IsNullOrWhiteSpace(contributionId))
        {
            throw new ArgumentException("Contribution id is required.", nameof(contributionId));
        }

        return new RecordedSessionSignalRowTarget(null, extensionId, contributionId);
    }

    private static string BuiltInStableKey(RecordedSessionBuiltInSignalRow row)
    {
        return row switch
        {
            RecordedSessionBuiltInSignalRow.Travel => "travel",
            RecordedSessionBuiltInSignalRow.Velocity => "velocity",
            RecordedSessionBuiltInSignalRow.Imu => "imu",
            RecordedSessionBuiltInSignalRow.PitchRoll => "pitch-roll",
            RecordedSessionBuiltInSignalRow.Speed => "speed",
            RecordedSessionBuiltInSignalRow.Elevation => "elevation",
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, null),
        };
    }
}
