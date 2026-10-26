using System;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

/// <summary>
/// The first click of a two-click alignment: the telemetry time where the
/// event shows in the aligned source. The second click marks where the same
/// event shows in the telemetry, and the difference corrects the source offset.
/// </summary>
internal abstract record TimelineAlignmentMark(double Seconds)
{
    public sealed record GpsTrack(double Seconds) : TimelineAlignmentMark(Seconds);

    public sealed record VideoClip(Guid ClipId, double Seconds) : TimelineAlignmentMark(Seconds);
}
