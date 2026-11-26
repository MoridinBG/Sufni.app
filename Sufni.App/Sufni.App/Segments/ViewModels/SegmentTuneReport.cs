using System;
using System.Collections.Immutable;
using System.Linq;
using Sufni.App.Sessions.Models;

namespace Sufni.App.Segments.ViewModels;

public sealed record SegmentTuneRow(string Label, string Current, string Matched);

/// <summary>The changed tune values of the two compared rides, with a note on the values that are the same or not recorded.</summary>
public sealed record SegmentTuneReport(ImmutableArray<SegmentTuneRow> Rows, string Note)
{
    public bool HasRows => !Rows.IsEmpty;

    public static SegmentTuneReport From(TuneComparison comparison)
    {
        var counts = $"Same: {comparison.Same}. Not recorded: {comparison.NotRecorded}.";
        var note = !comparison.Changes.IsEmpty ? counts
            : comparison.Same == 0 ? "Tune: not recorded."
            : $"No recorded value differs. {counts}";
        return new([.. comparison.Changes.Select(change => new SegmentTuneRow(Label(change.Field), change.Current, change.Matched))], note);
    }

    private static string Label(TuneField field) => field switch
    {
        TuneField.FrontSpringRate => "Front spring",
        TuneField.FrontHighSpeedCompression => "Front HSC",
        TuneField.FrontLowSpeedCompression => "Front LSC",
        TuneField.FrontLowSpeedRebound => "Front LSR",
        TuneField.FrontHighSpeedRebound => "Front HSR",
        TuneField.RearSpringRate => "Rear spring",
        TuneField.RearHighSpeedCompression => "Rear HSC",
        TuneField.RearLowSpeedCompression => "Rear LSC",
        TuneField.RearLowSpeedRebound => "Rear LSR",
        TuneField.RearHighSpeedRebound => "Rear HSR",
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
}
