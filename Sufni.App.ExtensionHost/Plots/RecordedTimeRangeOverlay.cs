using System.Collections.Generic;

namespace Sufni.App.Plots;

public static class RecordedTimeRangeOverlayIds
{
    public const string AnalysisRange = "analysis_range";
    public const string PreviewRange = "preview_range";
    public const string Airtime = "airtime";
    public const string StatisticsSelection = "statistics_selection";
}

public sealed record RecordedTimeRangeOverlayColor(
    byte A,
    byte R,
    byte G,
    byte B);

public sealed record RecordedTimeRangeOverlayStyle(
    RecordedTimeRangeOverlayColor FillColor,
    RecordedTimeRangeOverlayColor OutlineColor,
    float OutlineWidth);

public sealed record RecordedTimeRangeOverlayLabelOptions(
    double Y,
    double FontSize,
    bool CullCollisions);

public sealed record RecordedTimeRangeOverlay(
    double StartSeconds,
    double EndSeconds,
    string? Label = null,
    RecordedTimeRangeOverlayStyle? Style = null);

public sealed record RecordedTimeRangeOverlaySet(
    IReadOnlyList<RecordedTimeRangeOverlay> Ranges,
    RecordedTimeRangeOverlayStyle Style,
    RecordedTimeRangeOverlayLabelOptions? LabelOptions = null);

public sealed record RecordedTimeRangeOverlaySetRegistration(
    string Id,
    RecordedTimeRangeOverlaySet Set,
    bool IsVisible);
