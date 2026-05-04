using System;

namespace Sufni.App.Models;

/// <summary>
/// Identifies the raw-source format behind a recorded session.
/// Each kind has its own payload shape and reprocessing path.
/// </summary>
public enum RecordedSessionSourceKind
{
    ImportedSst = 1,
    LiveCapture = 2
}

/// <summary>
/// Converts recorded-source kinds to and from their database values.
/// The stored strings are stable identifiers, separate from enum member names.
/// </summary>
public static class RecordedSessionSourceKindExtensions
{
    public static string ToStorageValue(this RecordedSessionSourceKind sourceKind) => sourceKind switch
    {
        RecordedSessionSourceKind.ImportedSst => "imported_sst",
        RecordedSessionSourceKind.LiveCapture => "live_capture",
        _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unknown recorded session source kind.")
    };

    public static RecordedSessionSourceKind FromStorageValue(string value) => value switch
    {
        "imported_sst" => RecordedSessionSourceKind.ImportedSst,
        "live_capture" => RecordedSessionSourceKind.LiveCapture,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown recorded session source kind value.")
    };
}
