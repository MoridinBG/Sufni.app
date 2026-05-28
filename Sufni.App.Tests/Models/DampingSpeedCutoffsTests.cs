using Sufni.App.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Models;

public class DampingSpeedCutoffsTests
{
    [Fact]
    public void Default_UsesConfiguredDefaultForEverySideAndCircuit()
    {
        var cutoffs = DampingSpeedCutoffs.Default;

        Assert.Equal(200, cutoffs.Get(SuspensionType.Front, DampingSpeedCircuit.Compression));
        Assert.Equal(200, cutoffs.Get(SuspensionType.Front, DampingSpeedCircuit.Rebound));
        Assert.Equal(200, cutoffs.Get(SuspensionType.Rear, DampingSpeedCircuit.Compression));
        Assert.Equal(200, cutoffs.Get(SuspensionType.Rear, DampingSpeedCircuit.Rebound));
    }

    [Fact]
    public void With_UpdatesOnlyRequestedSideAndCircuit_AndClampsValue()
    {
        var cutoffs = DampingSpeedCutoffs.Default
            .With(SuspensionType.Rear, DampingSpeedCircuit.Rebound, 2500);

        Assert.Equal(200, cutoffs.Get(SuspensionType.Front, DampingSpeedCircuit.Compression));
        Assert.Equal(200, cutoffs.Get(SuspensionType.Front, DampingSpeedCircuit.Rebound));
        Assert.Equal(200, cutoffs.Get(SuspensionType.Rear, DampingSpeedCircuit.Compression));
        Assert.Equal(2000, cutoffs.Get(SuspensionType.Rear, DampingSpeedCircuit.Rebound));
    }

    [Theory]
    [InlineData(-4, 0)]
    [InlineData(204, 200)]
    [InlineData(205, 210)]
    [InlineData(1996, 2000)]
    [InlineData(2500, 2000)]
    public void RoundDragValue_ClampsAndRoundsToNearestStep(double value, double expected)
    {
        Assert.Equal(expected, DampingSpeedCutoffs.RoundDragValue(value));
    }
}
