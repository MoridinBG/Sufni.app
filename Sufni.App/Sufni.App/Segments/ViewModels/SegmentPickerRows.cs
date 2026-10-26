using System;
using System.Globalization;
using Sufni.Telemetry;

namespace Sufni.App.Segments.ViewModels;

public sealed record SavedSegmentRow(Guid Id, string Name, TelemetryTimeRange Range, bool IsSelected)
{
    public string RangeText => SegmentRangeText.Format(Range);
}

public sealed record StravaSegmentRow(string Name, TelemetryTimeRange Range, double DistanceMeters)
{
    public string RangeText => SegmentRangeText.Format(Range);

    public string DistanceText => DistanceMeters > 0
        ? string.Create(CultureInfo.InvariantCulture, $"{DistanceMeters:F0} m")
        : string.Empty;
}

internal static class SegmentRangeText
{
    public static string Format(TelemetryTimeRange range) =>
        string.Create(CultureInfo.InvariantCulture, $"{range.StartSeconds:F1}-{range.EndSeconds:F1}s");
}
