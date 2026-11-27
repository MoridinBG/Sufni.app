using System;
using System.Globalization;
using Sufni.App.Terrain.Formatting;
using Sufni.Telemetry;

namespace Sufni.App.Segments.ViewModels;

public sealed record SavedSegmentRow(Guid Id, string Name, TelemetryTimeRange Range, bool IsSelected)
{
    public string RangeText => SegmentRangeText.Format(Range);
}

/// <summary>A label of the session. <see cref="Reading"/> is how its stretch reads on the scale of everything else.</summary>
public sealed record TerrainLabelRow(Guid Id, RoughnessClass Class, TelemetryTimeRange Range, bool IsSelected, LabelReading? Reading = null)
{
    public string ClassName => TerrainClassText.Name(Class);
    public string RangeText => SegmentRangeText.Format(Range);
    public string? ReadsText => TerrainClassText.ReadsAs(Class, Reading);
    public bool Clashes => Reading?.Clashes == true;
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
