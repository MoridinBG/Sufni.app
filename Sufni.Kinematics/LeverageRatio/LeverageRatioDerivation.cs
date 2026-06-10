using System;
using System.Collections.Generic;

namespace Sufni.Kinematics;

internal static class LeverageRatioDerivation
{
    public static CoordinateList DeriveData(
        IReadOnlyList<double> shockTravel,
        IReadOnlyList<double> wheelTravel)
    {
        var samples = DeriveSamples(shockTravel, wheelTravel);
        List<double> sampleWheelTravel = [];
        List<double> ratios = [];

        foreach (var sample in samples)
        {
            sampleWheelTravel.Add(sample.WheelTravelMm);
            ratios.Add(sample.Ratio);
        }

        return new CoordinateList(sampleWheelTravel, ratios);
    }

    public static IReadOnlyList<LeverageRatioSample> DeriveSamples(
        IReadOnlyList<double> shockTravel,
        IReadOnlyList<double> wheelTravel)
    {
        ArgumentNullException.ThrowIfNull(shockTravel);
        ArgumentNullException.ThrowIfNull(wheelTravel);

        if (shockTravel.Count < 2 || shockTravel.Count != wheelTravel.Count)
        {
            throw new ArgumentException("Travel curves must contain at least two matching shock and wheel travel samples.");
        }

        List<LeverageRatioSample> samples = [];
        for (var index = 1; index < wheelTravel.Count; index++)
        {
            var wheelTravelDelta = wheelTravel[index] - wheelTravel[index - 1];
            var shockTravelDelta = shockTravel[index] - shockTravel[index - 1];
            samples.Add(new LeverageRatioSample(
                WheelTravelMm: (wheelTravel[index - 1] + wheelTravel[index]) / 2.0,
                Ratio: wheelTravelDelta / shockTravelDelta));
        }

        return samples;
    }
}
