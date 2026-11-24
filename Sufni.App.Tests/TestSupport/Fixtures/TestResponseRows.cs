using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Fixtures;

/// <summary>Response rows against a fitted baseline, for report and view tests. Unnamed targets match the baseline.</summary>
internal static class TestResponseRows
{
    public static TerrainResponseRow Row(RoughnessClass? group, double metres, params ResponseCell[] cells) =>
        new(group, metres, 0.5, 5, [.. Enum.GetValues<ResponseTarget>()
            .Select(target => cells.FirstOrDefault(cell => cell.Target == target) ?? Cell(target, 1))]);

    public static TerrainResponseRow TooShort(RoughnessClass? group) =>
        new(group, 100, 0.1, 5, [.. Enum.GetValues<ResponseTarget>().Select(target =>
            new ResponseCell(target, ImuLocation.Frame, ResponseCellState.TooShort, null, 6, 1200))]);

    public static ResponseCell Cell(ResponseTarget target, double ratio) =>
        new(target, ImuLocation.Frame, ResponseCellState.WithinRepeatRange, ratio, 6, 1200);

    public static ResponseCell Finding(ResponseTarget target, double ratio) =>
        new(target, ImuLocation.Frame, ResponseCellState.Finding, ratio, 6, 1200);
}
