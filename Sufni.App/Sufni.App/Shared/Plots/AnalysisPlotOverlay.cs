using System.Collections.Generic;

namespace Sufni.App.Shared.Plots;

/// <summary>
/// A comparison drawn over an analysis plot in the comparison color: a trace
/// through plot coordinates and statistic markers.
/// </summary>
public sealed record AnalysisPlotOverlay(
    double[] TraceX,
    double[] TraceY,
    IReadOnlyList<AnalysisPlotMarker> Markers);

/// <summary>
/// A dashed line across the plot at <see cref="Y"/>, labeled at the right edge
/// above or below the line.
/// </summary>
public sealed record AnalysisPlotMarker(double Y, string Text, bool LabelAbove);
