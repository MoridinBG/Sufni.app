using System.Collections.Generic;

namespace Sufni.App.Models;

public static class TelemetrySourceKeys
{
    public const string Front = "front";
    public const string Rear = "rear";

    public static string ImuLocation(int locationId) => $"location:{locationId}";
}

public sealed class TelemetrySourceVisibilityStore
{
    private readonly HashSet<TelemetrySourceVisibilityKey> hiddenSources = [];

    public bool IsVisible(string rowId, string sourceKey)
    {
        return !hiddenSources.Contains(new TelemetrySourceVisibilityKey(rowId, sourceKey));
    }

    public void SetVisible(string rowId, string sourceKey, bool visible)
    {
        var key = new TelemetrySourceVisibilityKey(rowId, sourceKey);
        if (visible)
        {
            hiddenSources.Remove(key);
        }
        else
        {
            hiddenSources.Add(key);
        }
    }

    public void Clear()
    {
        hiddenSources.Clear();
    }

    private readonly record struct TelemetrySourceVisibilityKey(string RowId, string SourceKey);
}
