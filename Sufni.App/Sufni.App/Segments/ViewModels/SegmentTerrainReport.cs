using System;
using System.Linq;
using Sufni.App.Segments.Models;
using Sufni.App.Terrain.Formatting;
using Sufni.App.Terrain.Models;

namespace Sufni.App.Segments.ViewModels;

/// <summary>The same-trail comparison: the pace line, then the response report against the matched run.</summary>
public sealed record SegmentTerrainReport(string? Message, string? Pace, TerrainResponseReport? Response)
{
    public bool HasMessage => Message is not null;
    public bool HasPace => Pace is not null;
    public bool HasResponse => Response is not null;

    public static SegmentTerrainReport From(SegmentTerrainComparison terrain) => terrain switch
    {
        SegmentTerrainComparison.Compared compared => new(
            compared.AverageRunCount is { } n ? $"Classes from the average of {n} runs" : null,
            compared.Rows.FirstOrDefault(row => row.Class is null)?.Pace is { } pace ? TerrainResponseReports.Pace(pace) : null,
            TerrainResponseReports.AgainstMatchedRun(compared.Rows)),
        SegmentTerrainComparison.Unavailable unavailable => new(unavailable.Reason switch
        {
            SegmentTerrainUnavailable.NoSourceProfile => "This ride has no current terrain profile.",
            SegmentTerrainUnavailable.NoMatchProfile => "The matched ride has no current terrain profile.",
            SegmentTerrainUnavailable.NoCommonSensor => "The rides have no shared IMU location.",
            SegmentTerrainUnavailable.Unaligned => "The terrain signals could not be aligned.",
            _ => throw new ArgumentOutOfRangeException(nameof(terrain))
        }, null, null),
        SegmentTerrainComparison.Failed => new("Terrain comparison failed. Reopen the comparison to retry.", null, null)
    };
}
