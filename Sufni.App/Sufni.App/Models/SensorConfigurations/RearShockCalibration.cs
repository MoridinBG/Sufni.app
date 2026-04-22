using System;

namespace Sufni.App.Models.SensorConfigurations;

internal sealed record RearShockCalibration(
    double MaxShockStroke,
    Func<ushort, double> MeasurementToShockStroke);

internal sealed record RearSuspensionMapping(
    double MaxWheelTravel,
    Func<double, double> ShockStrokeToWheelTravel);

internal sealed record RearTravelCalibration(
    double MaxTravel,
    Func<ushort, double> MeasurementToTravel);