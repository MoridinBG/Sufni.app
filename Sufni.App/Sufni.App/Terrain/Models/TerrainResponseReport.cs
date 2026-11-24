using System.Collections.Immutable;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public sealed record ResponseVerdict(ResponseFinding Finding, string Text);

/// <summary>One response of a chart row against the repeat range that its reference allows.</summary>
public sealed record ResponseBar(ResponseTarget Target, string Label, ResponseCellState State, double? Deviation,
    double RepeatRange, string Value)
{
    public bool IsFinding => State == ResponseCellState.Finding;
}

public sealed record ResponseChartRow(RoughnessClass? Group, string Label, string Coverage, ResponseConfidence Confidence,
    ImmutableArray<ResponseBar> Fork, ImmutableArray<ResponseBar> Shock)
{
    public bool IsLowConfidence => Confidence == ResponseConfidence.Low;
}

public sealed record ResponseDetailValue(string Label, string Value, string? Source = null);
public sealed record ResponseDetailRow(string Label, ImmutableArray<ResponseDetailValue> Values);

/// <summary>
/// Suspension response against one reference: observation verdicts per end, a deviation chart per ground group with
/// one shared scale, and every figure in the details. A summary line replaces the verdicts when there are none.
/// </summary>
public sealed record TerrainResponseReport(ImmutableArray<ResponseVerdict> Fork, ImmutableArray<ResponseVerdict> Shock,
    string? Summary, double Scale, ImmutableArray<ResponseChartRow> Chart, ImmutableArray<ResponseDetailRow> Details)
{
    public bool HasForkVerdicts => !Fork.IsEmpty;
    public bool HasShockVerdicts => !Shock.IsEmpty;
    public bool HasChart => !Chart.IsEmpty;
}
