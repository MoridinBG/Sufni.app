using System;
using System.Collections.Generic;
using System.Linq;

namespace Sufni.Kinematics;

public sealed record LeverageRatioValidationError(int? PointIndex, string Message);

public sealed class LeverageRatioValidationException : Exception
{
    public LeverageRatioValidationException(IReadOnlyList<LeverageRatioValidationError> errors)
        : base(string.Join(Environment.NewLine, errors.Select(error => error.Message)))
    {
        Errors = errors;
    }

    public IReadOnlyList<LeverageRatioValidationError> Errors { get; }
}

public static class LeverageRatioValidation
{
    public static IReadOnlyList<LeverageRatioValidationError> Validate(IReadOnlyList<LeverageRatioPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        List<LeverageRatioValidationError> errors = [];
        if (points.Count < 2)
        {
            errors.Add(new LeverageRatioValidationError(null, "At least two points are required."));
        }

        if (points.Count == 0)
        {
            return errors;
        }

        if (points[0].ShockTravelMm < 0)
        {
            errors.Add(new LeverageRatioValidationError(0, "Shock travel must be zero or greater."));
        }

        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            if (current.ShockTravelMm == previous.ShockTravelMm)
            {
                errors.Add(new LeverageRatioValidationError(index, "Shock travel must increase monotonically; duplicate values are not allowed."));
            }
            else if (current.ShockTravelMm < previous.ShockTravelMm)
            {
                errors.Add(new LeverageRatioValidationError(index, "Shock travel must increase monotonically."));
            }

            if (current.WheelTravelMm < previous.WheelTravelMm)
            {
                errors.Add(new LeverageRatioValidationError(index, "Wheel travel must not decrease."));
            }
        }

        return errors;
    }
}