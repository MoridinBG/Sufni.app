using System;

namespace Sufni.App.Models.SensorConfigurations;

internal static class LinearSensorCalibrationMath
{
    public static double MeasurementToStroke(double length, int resolution)
    {
        return length / (Math.Pow(2.0, resolution) - 1.0);
    }
}