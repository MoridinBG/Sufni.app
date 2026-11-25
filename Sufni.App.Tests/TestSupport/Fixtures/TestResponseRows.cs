using System.Collections.Immutable;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Fixtures;

/// <summary>Response rows against a baseline or a matched run, for report and view tests. Unnamed targets match their reference.</summary>
internal static class TestResponseRows
{
    public static TerrainResponseRow Row(RoughnessClass? group, double metres, params ResponseCell[] cells) =>
        new(group, metres, 0.5, 5, [.. Enum.GetValues<ResponseTarget>()
            .Select(target => cells.FirstOrDefault(cell => cell.Target == target) ?? Cell(target, 1))]);

    public static TerrainResponseRow TooShort(RoughnessClass? group) =>
        new(group, 100, 0.1, 5, [.. Enum.GetValues<ResponseTarget>().Select(target =>
            new ResponseCell(target, GroundSensors.Frame, ResponseCellState.TooShort, null, 6, 1200))]);

    public static ResponseCell Cell(ResponseTarget target, double ratio) =>
        new(target, GroundSensors.Frame, ResponseCellState.WithinRepeatRange, ratio, 6, 1200);

    public static ResponseCell Finding(ResponseTarget target, double ratio) =>
        new(target, GroundSensors.Frame, ResponseCellState.Finding, ratio, 6, 1200);

    public static TerrainComparisonRow Compared(RoughnessClass? group, double metres, PaceQuartiles? pace,
        params (ResponseTarget Target, ComparisonRatio Ratio)[] ratios)
    {
        var within = new ComparisonRatio(ResponseCellState.WithinRepeatRange, 1.01);
        var named = ratios.ToDictionary(r => r.Target, r => r.Ratio);
        return new(group, metres, pace, Enum.GetValues<ResponseTarget>().ToImmutableDictionary(target => target,
            target => named.GetValueOrDefault(target, within)), within, within);
    }

    /// <summary>A usual that predicts the same response for every target on any ground.</summary>
    public static SuspensionResponseModel Usual(double predicted = 100) =>
        new(Enum.GetValues<ResponseTarget>().ToImmutableDictionary(target => target,
            target => (ResponseFit)new ResponseFit.Fitted(GroundSensors.Frame, 6, 1200, [Math.Log(predicted), 0, 0, 0, 0])), 6, 1200);
}
