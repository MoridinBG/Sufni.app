using System;

namespace Sufni.App.Models;

public enum RecordedSessionSourceKind
{
    ImportedSst = 1,
    LiveCapture = 2
}

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
