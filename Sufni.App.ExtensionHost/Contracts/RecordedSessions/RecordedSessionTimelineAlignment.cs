namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public enum RecordedSessionTimelineAlignmentTarget
{
    None,
    GpsTrack,
    ExternalMedia,
}

public sealed record RecordedSessionTimelineAlignmentMark(
    RecordedSessionTimelineAlignmentTarget Target,
    string? SubjectId,
    double Seconds);

public sealed record RecordedSessionTimelineAlignmentState(
    RecordedSessionTimelineAlignmentMark? PendingMark)
{
    public static RecordedSessionTimelineAlignmentState Empty { get; } = new(PendingMark: null);

    public bool HasPendingMark => PendingMark is not null;

    public bool IsPendingFor(RecordedSessionTimelineAlignmentTarget target, string? subjectId = null)
    {
        return PendingMark is { } mark &&
               mark.Target == target &&
               string.Equals(mark.SubjectId, subjectId, System.StringComparison.Ordinal);
    }
}

public sealed record RecordedSessionTimelineAlignmentResolution(
    RecordedSessionTimelineAlignmentTarget Target,
    string? SubjectId,
    double MarkedSeconds,
    double ResolvedSeconds,
    double OffsetDeltaSeconds);
