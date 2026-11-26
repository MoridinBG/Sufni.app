using System;
using System.Collections.Immutable;
using System.Globalization;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Sessions.Models;

/// <summary>The spring and damper fields of a session, in the order of the entry views.</summary>
public enum TuneField
{
    FrontSpringRate,
    FrontHighSpeedCompression,
    FrontLowSpeedCompression,
    FrontLowSpeedRebound,
    FrontHighSpeedRebound,
    RearSpringRate,
    RearHighSpeedCompression,
    RearLowSpeedCompression,
    RearLowSpeedRebound,
    RearHighSpeedRebound
}

public sealed record TuneChange(TuneField Field, string Current, string Matched);

public sealed record TuneComparison(ImmutableArray<TuneChange> Changes, int Same, int NotRecorded);

/// <summary>The recorded spring and damper values of a session. A spring text is trimmed; an empty one is not recorded.</summary>
public sealed record SessionTune(
    string? FrontSpringRate,
    uint? FrontHighSpeedCompression,
    uint? FrontLowSpeedCompression,
    uint? FrontLowSpeedRebound,
    uint? FrontHighSpeedRebound,
    string? RearSpringRate,
    uint? RearHighSpeedCompression,
    uint? RearLowSpeedCompression,
    uint? RearLowSpeedRebound,
    uint? RearHighSpeedRebound)
{
    public static SessionTune From(SessionSnapshot session) => new(
        Spring(session.FrontSpringRate),
        session.FrontHighSpeedCompression,
        session.FrontLowSpeedCompression,
        session.FrontLowSpeedRebound,
        session.FrontHighSpeedRebound,
        Spring(session.RearSpringRate),
        session.RearHighSpeedCompression,
        session.RearLowSpeedCompression,
        session.RearLowSpeedRebound,
        session.RearHighSpeedRebound);

    /// <summary>The recorded value as text, or null when the field is not recorded.</summary>
    public string? ValueOf(TuneField field) => field switch
    {
        TuneField.FrontSpringRate => FrontSpringRate,
        TuneField.FrontHighSpeedCompression => Number(FrontHighSpeedCompression),
        TuneField.FrontLowSpeedCompression => Number(FrontLowSpeedCompression),
        TuneField.FrontLowSpeedRebound => Number(FrontLowSpeedRebound),
        TuneField.FrontHighSpeedRebound => Number(FrontHighSpeedRebound),
        TuneField.RearSpringRate => RearSpringRate,
        TuneField.RearHighSpeedCompression => Number(RearHighSpeedCompression),
        TuneField.RearLowSpeedCompression => Number(RearLowSpeedCompression),
        TuneField.RearLowSpeedRebound => Number(RearLowSpeedRebound),
        TuneField.RearHighSpeedRebound => Number(RearHighSpeedRebound),
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    /// <summary>
    /// Compares the fields in the order of <see cref="TuneField"/>. A field that one ride does not record counts as not
    /// recorded; texts compare without case.
    /// </summary>
    public static TuneComparison Compare(SessionTune current, SessionTune matched)
    {
        var changes = ImmutableArray.CreateBuilder<TuneChange>();
        int same = 0, notRecorded = 0;
        foreach (var field in Enum.GetValues<TuneField>())
        {
            if (current.ValueOf(field) is not { } now || matched.ValueOf(field) is not { } then) notRecorded++;
            else if (string.Equals(now, then, StringComparison.OrdinalIgnoreCase)) same++;
            else changes.Add(new(field, now, then));
        }
        return new(changes.ToImmutable(), same, notRecorded);
    }

    private static string? Spring(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? Number(uint? value) => value?.ToString(CultureInfo.InvariantCulture);
}
