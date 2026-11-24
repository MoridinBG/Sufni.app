using System;
using System.Collections.Immutable;
using System.Linq;
using Sufni.App.Segments.Models;
using Sufni.App.Terrain.Formatting;
using Sufni.Telemetry;

namespace Sufni.App.Segments.ViewModels;

public sealed record SegmentTerrainValue(string Text, bool Muted = false)
{
    public string? Hint => Muted ? "within repeat range" : null;
}

public sealed record SegmentTerrainColumn(ImmutableArray<SegmentTerrainValue> Values);
public sealed record SegmentTerrainTableRow(ImmutableArray<SegmentTerrainColumn> Columns);
public sealed record SegmentTerrainTable(string? Message, ImmutableArray<SegmentTerrainTableRow> Rows)
{
    public static ImmutableArray<string> Headers { get; } =
        ["Terrain", "Distance", "Pace · median (Q1–Q3)", .. Enum.GetValues<ResponseTarget>().Select(TerrainResponseText.TargetLabel), "Same position"];
    public bool HasMessage => Message is not null;
    public bool HasRows => !Rows.IsEmpty;

    public static SegmentTerrainTable From(SegmentTerrainComparison terrain) => terrain switch
    {
        SegmentTerrainComparison.Compared compared => new(compared.AverageRunCount is { } n ? $"Classes from the average of {n} runs" : null, [.. compared.Rows.Select(Row)]),
        SegmentTerrainComparison.Unavailable unavailable => new(unavailable.Reason switch
        {
            SegmentTerrainUnavailable.NoSourceProfile => "This ride has no current terrain profile.",
            SegmentTerrainUnavailable.NoMatchProfile => "The matched ride has no current terrain profile.",
            SegmentTerrainUnavailable.NoCommonSensor => "The rides have no shared IMU location.",
            SegmentTerrainUnavailable.Unaligned => "The terrain signals could not be aligned.",
            _ => throw new ArgumentOutOfRangeException(nameof(terrain))
        }, []),
        SegmentTerrainComparison.Failed => new("Terrain comparison failed. Reopen the comparison to retry.", [])
    };

    private static SegmentTerrainTableRow Row(TerrainComparisonRow row)
    {
        static SegmentTerrainColumn Text(string value) => new([new(value)]);
        static SegmentTerrainValue Ratio(ComparisonRatio value, string prefix = "") =>
            new(prefix + TerrainResponseText.Value(value.State, value.Ratio), value.State == ResponseCellState.WithinRepeatRange);
        return new([
            Text(row.Class?.ToString() ?? "All"), Text($"{row.DistanceMetres:0} m"),
            Text(row.Pace is { } pace ? $"{pace.Median:0.00}× ({pace.Lower:0.00}–{pace.Upper:0.00})" : "Under 300 m"),
            .. Enum.GetValues<ResponseTarget>().Select(target => new SegmentTerrainColumn([Ratio(row.Responses[target])])),
            new([Ratio(row.FrontSamePosition, "Front "), Ratio(row.RearSamePosition, "Rear ")])
        ]);
    }
}
