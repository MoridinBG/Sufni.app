using System;
using System.Collections.Generic;

namespace Sufni.Kinematics;

public static class TravelInterpolation
{
    public static double WheelTravelAt(CoordinateList curve, double shockStroke)
    {
        return WheelTravelAt(curve.X, curve.Y, shockStroke);
    }

    public static double WheelTravelAt(
        IReadOnlyList<double> shockTravel,
        IReadOnlyList<double> wheelTravel,
        double shockStroke)
    {
        ArgumentNullException.ThrowIfNull(shockTravel);
        ArgumentNullException.ThrowIfNull(wheelTravel);

        if (shockTravel.Count == 0 || shockTravel.Count != wheelTravel.Count)
        {
            throw new ArgumentException("Travel curves must contain matching shock and wheel travel samples.");
        }

        if (shockStroke <= shockTravel[0])
        {
            return wheelTravel[0];
        }

        if (shockStroke >= shockTravel[^1])
        {
            return wheelTravel[^1];
        }

        for (var index = 1; index < shockTravel.Count; index++)
        {
            if (shockStroke > shockTravel[index])
            {
                continue;
            }

            var progress = (shockStroke - shockTravel[index - 1]) / (shockTravel[index] - shockTravel[index - 1]);
            return wheelTravel[index - 1] + (wheelTravel[index] - wheelTravel[index - 1]) * progress;
        }

        return wheelTravel[^1];
    }
}
