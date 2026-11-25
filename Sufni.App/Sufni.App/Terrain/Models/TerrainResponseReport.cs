using System.Collections.Immutable;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public sealed record ResponseVerdict(ResponseFinding Finding, string Text);

/// <summary>
/// One response of a chart row against the repeat range that its reference allows. A null target is activity at the
/// same trail position.
/// </summary>
public sealed record ResponseBar(ResponseTarget? Target, string Label, string Explanation, ResponseCellState State,
    double? Deviation, double RepeatRange, string Value)
{
    public bool IsFinding => State == ResponseCellState.Finding;
}

/// <summary>One ground group of the chart; the coverage note adds what the coverage line cannot show, if anything.</summary>
public sealed record ResponseChartRow(RoughnessClass? Group, string Label, string Coverage, string? CoverageNote,
    ResponseConfidence Confidence, ImmutableArray<ResponseBar> Fork, ImmutableArray<ResponseBar> Shock)
{
    public bool IsLowConfidence => Confidence == ResponseConfidence.Low;
}

/// <summary>
/// Suspension response against one reference: observation verdicts per end, a guide to reading the chart, and a
/// deviation chart per ground group with one shared scale from <see cref="ScaleStart"/> to <see cref="ScaleEnd"/>.
/// A summary line replaces the verdicts when there are none.
/// </summary>
public sealed record TerrainResponseReport(ImmutableArray<ResponseVerdict> Fork, ImmutableArray<ResponseVerdict> Shock,
    string? Summary, string Guide, double Scale, string ScaleStart, string ScaleEnd, ImmutableArray<ResponseChartRow> Chart)
{
    public bool HasForkVerdicts => !Fork.IsEmpty;
    public bool HasShockVerdicts => !Shock.IsEmpty;
    public bool HasChart => !Chart.IsEmpty;
}
