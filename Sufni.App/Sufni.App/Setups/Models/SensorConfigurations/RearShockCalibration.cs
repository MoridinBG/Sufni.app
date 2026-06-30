using System;

namespace Sufni.App.Setups.Models.SensorConfigurations;

internal sealed record RearTravelCalibration(
    double MaxTravel,
    Func<ushort, double> MeasurementToTravel,
    bool MeasurementWraps);