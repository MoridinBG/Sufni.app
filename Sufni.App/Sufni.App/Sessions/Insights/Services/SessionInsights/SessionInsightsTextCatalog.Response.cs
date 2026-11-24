using System.Collections.Generic;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Insights.Services.SessionInsights;

internal static partial class SessionInsightsTextCatalog
{
    public static string ResponseDescription(TerrainResponseDiagnostics report) => report.State switch
    {
        SessionResponseState.BaselineError => "Your usual could not be loaded.",
        SessionResponseState.InsufficientBaseline when report.LacksVariation =>
            "Your usual needs rides where speed and roughness vary independently.",
        SessionResponseState.InsufficientBaseline when report.Sessions < SuspensionResponseModel.MinimumSessions =>
            $"Your usual needs {SuspensionResponseModel.MinimumSessions} rides on this bike: {report.Sessions} of {SuspensionResponseModel.MinimumSessions} so far.",
        SessionResponseState.InsufficientBaseline =>
            $"Your usual needs {SuspensionResponseModel.MinimumWindows:N0} usable windows: {report.Windows:N0} so far.",
        SessionResponseState.Reported =>
            $"Your usual comes from {report.Sessions} other rides on this bike ({report.Windows:N0} windows).",
        _ => string.Empty
    };

    public static string? GroundComfortNote(SessionTerrainInput? terrain) => terrain switch
    {
        null => "Ground & comfort needs a terrain profile for this ride.",
        { Scope: null } => "Ground & comfort is not available in a segment comparison.",
        _ => null
    };

    public static string? CharacterHint(TerrainCharacter character)
    {
        var parts = new List<string>();
        Add(character.Rear, "rear");
        Add(character.Front, "fork");
        return parts.Count == 0 ? null : $"Experimental: most rough ground here was {string.Join(" and ", parts)}.";

        void Add(CharacterShares? shares, string end)
        {
            if (shares?.Dominant is not { } dominant) return;
            var (name, share) = dominant == StrokeCharacter.BigHits ? ("big hits", shares.BigHitShare) : ("chatter", shares.ChatterShare);
            parts.Add($"{name} at the {end} ({share:P0})");
        }
    }
}
