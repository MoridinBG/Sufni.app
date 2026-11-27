using Sufni.Telemetry;

namespace Sufni.App.Sessions.Models;

/// <summary>The stored form of a session's sensor decisions: null when there are none.</summary>
internal static class SensorDecisionsJson
{
    public static SensorDecisions Parse(string? json) =>
        json is null ? SensorDecisions.None : AppJson.Deserialize<SensorDecisions>(json) ?? SensorDecisions.None;

    public static string? Write(SensorDecisions decisions) =>
        decisions.IsEmpty ? null : AppJson.Serialize(decisions);
}
