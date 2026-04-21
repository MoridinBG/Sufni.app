using System;
using Sufni.Kinematics;

namespace Sufni.App.Models;

internal static class LeverageRatioShockStrokeRules
{
    private const double MatchTolerance = 0.01;

    public static bool TryValidate(
        double? shockStroke,
        LeverageRatio leverageRatio,
        out double validatedShockStroke,
        out string? errorMessage)
    {
        if (shockStroke is null)
        {
            validatedShockStroke = 0;
            errorMessage = "Shock stroke is required for leverage ratio bikes.";
            return false;
        }

        var expectedShockStroke = leverageRatio.MaxShockStroke;
        if (Math.Abs(shockStroke.Value - expectedShockStroke) > MatchTolerance)
        {
            validatedShockStroke = 0;
            errorMessage = $"Shock stroke must match leverage ratio max shock stroke of {expectedShockStroke:0.###} mm.";
            return false;
        }

        validatedShockStroke = shockStroke.Value;
        errorMessage = null;
        return true;
    }
}