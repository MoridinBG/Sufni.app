using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Models;

public class SessionDamperPercentagesTests
{
    [Fact]
    public void Get_ReturnsBandPercentage_ForRequestedSide()
    {
        var percentages = new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8);

        Assert.Equal(1, percentages.Get(SuspensionType.Front, DamperBand.Hsc));
        Assert.Equal(3, percentages.Get(SuspensionType.Front, DamperBand.Lsc));
        Assert.Equal(5, percentages.Get(SuspensionType.Front, DamperBand.Lsr));
        Assert.Equal(7, percentages.Get(SuspensionType.Front, DamperBand.Hsr));
        Assert.Equal(2, percentages.Get(SuspensionType.Rear, DamperBand.Hsc));
        Assert.Equal(4, percentages.Get(SuspensionType.Rear, DamperBand.Lsc));
        Assert.Equal(6, percentages.Get(SuspensionType.Rear, DamperBand.Lsr));
        Assert.Equal(8, percentages.Get(SuspensionType.Rear, DamperBand.Hsr));
    }

    [Fact]
    public void FromSides_PreservesFrontAndRearBandOrder()
    {
        var percentages = SessionDamperPercentages.FromSides(
            new SessionDamperSidePercentages(1, 2, 3, 4),
            new SessionDamperSidePercentages(5, 6, 7, 8));

        Assert.Equal(new SessionDamperPercentages(1, 5, 2, 6, 3, 7, 4, 8), percentages);
    }
}
