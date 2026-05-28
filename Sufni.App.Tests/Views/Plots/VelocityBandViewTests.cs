using Avalonia.Controls;
using Sufni.App.SessionDetails;
using Sufni.App.Views.Plots;

namespace Sufni.App.Tests.Views.Plots;

public class VelocityBandViewTests
{
    [Fact]
    public void ZoneLengths_AlignToVelocityThresholds()
    {
        var sut = new VelocityBandView();

        Assert.Equal(GridUnitType.Star, sut.HighSpeedZoneLength.GridUnitType);
        Assert.Equal(GridUnitType.Star, sut.LowSpeedZoneLength.GridUnitType);
        Assert.Equal(
            SessionDampingSettings.VelocityHistogramLimitMmPerSecond -
            SessionDampingSettings.HighSpeedThresholdMmPerSecond,
            sut.HighSpeedZoneLength.Value);
        Assert.Equal(
            SessionDampingSettings.HighSpeedThresholdMmPerSecond,
            sut.LowSpeedZoneLength.Value);
    }
}
