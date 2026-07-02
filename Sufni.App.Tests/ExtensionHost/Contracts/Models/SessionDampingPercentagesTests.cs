using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;

namespace Sufni.App.Tests.ExtensionHost.Contracts.Models;

public class SessionDampingPercentagesTests
{
    [Fact]
    public void Get_ReturnsBandPercentage_ForRequestedSide()
    {
        var percentages = new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8);

        Assert.Equal(1, percentages.Get(SuspensionType.Front, DampingBand.Hsc));
        Assert.Equal(3, percentages.Get(SuspensionType.Front, DampingBand.Lsc));
        Assert.Equal(5, percentages.Get(SuspensionType.Front, DampingBand.Lsr));
        Assert.Equal(7, percentages.Get(SuspensionType.Front, DampingBand.Hsr));
        Assert.Equal(2, percentages.Get(SuspensionType.Rear, DampingBand.Hsc));
        Assert.Equal(4, percentages.Get(SuspensionType.Rear, DampingBand.Lsc));
        Assert.Equal(6, percentages.Get(SuspensionType.Rear, DampingBand.Lsr));
        Assert.Equal(8, percentages.Get(SuspensionType.Rear, DampingBand.Hsr));
    }

    [Fact]
    public void FromSides_PreservesFrontAndRearBandOrder()
    {
        var percentages = SessionDampingPercentages.FromSides(
            new SessionDampingSidePercentages(1, 2, 3, 4),
            new SessionDampingSidePercentages(5, 6, 7, 8));

        Assert.Equal(new SessionDampingPercentages(1, 5, 2, 6, 3, 7, 4, 8), percentages);
    }
}
