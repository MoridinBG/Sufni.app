using System;

namespace Sufni.App.Models.SensorConfigurations;

internal sealed record RearTravelCalibration(
    double MaxTravel,
    Func<ushort, double> MeasurementToTravel);