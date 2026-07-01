
using Sufni.App.Setups.Models.SensorConfigurations;
namespace Sufni.App.Tests.Setups.Models.SensorConfigurations;

public class LinearSensorCalibrationMathTests
{
    [Fact]
    public void MeasurementToStroke_ReturnsLengthPerAdcStep()
    {
        var strokePerStep = LinearSensorCalibrationMath.MeasurementToStroke(length: 100, resolution: 10);

        Assert.Equal(100.0 / 1023.0, strokePerStep, precision: 12);
    }

    [Fact]
    public void MeasurementToStroke_ReturnsZero_WhenSensorLengthIsZero()
    {
        var strokePerStep = LinearSensorCalibrationMath.MeasurementToStroke(length: 0, resolution: 12);

        Assert.Equal(0, strokePerStep);
    }

    [Fact]
    public void MeasurementToStroke_DecreasesStepSize_WhenResolutionIncreases()
    {
        var tenBitStep = LinearSensorCalibrationMath.MeasurementToStroke(length: 150, resolution: 10);
        var twelveBitStep = LinearSensorCalibrationMath.MeasurementToStroke(length: 150, resolution: 12);

        Assert.True(twelveBitStep < tenBitStep);
        Assert.Equal(150.0 / 4095.0, twelveBitStep, precision: 12);
    }
}
