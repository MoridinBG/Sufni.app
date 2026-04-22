using System;
using System.Collections.Generic;
using Sufni.App.Models;
using Sufni.Kinematics;

namespace Sufni.App.Models.SensorConfigurations;

internal static class RearSuspensionMappingFactory
{
    public static RearSuspensionMapping Build(RearSuspension rearSuspension)
    {
        return rearSuspension switch
        {
            LinkageRearSuspension linkageRearSuspension => BuildLinkageMapping(linkageRearSuspension.Linkage),
            LeverageRatioRearSuspension leverageRatioRearSuspension => new RearSuspensionMapping(
                leverageRatioRearSuspension.LeverageRatio.MaxWheelTravel,
                leverageRatioRearSuspension.LeverageRatio.WheelTravelAt),
            _ => throw new ArgumentOutOfRangeException(nameof(rearSuspension))
        };
    }

    private static RearSuspensionMapping BuildLinkageMapping(Linkage linkage)
    {
        var solution = new KinematicSolver(linkage).SolveSuspensionMotion();
        var dataset = new BikeCharacteristics(solution).ShockStrokeToWheelTravelDataset();
        return new RearSuspensionMapping(
            dataset.Y[^1],
            shockStroke => Interpolate(dataset.X, dataset.Y, shockStroke));
    }

    private static double Interpolate(IReadOnlyList<double> x, IReadOnlyList<double> y, double value)
    {
        if (value <= x[0])
        {
            return y[0];
        }

        if (value >= x[^1])
        {
            return y[^1];
        }

        for (var index = 1; index < x.Count; index++)
        {
            if (value > x[index])
            {
                continue;
            }

            var progress = (value - x[index - 1]) / (x[index] - x[index - 1]);
            return y[index - 1] + (y[index] - y[index - 1]) * progress;
        }

        return y[^1];
    }
}