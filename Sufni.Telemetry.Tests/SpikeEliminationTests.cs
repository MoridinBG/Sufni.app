using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class SpikeEliminationTests
{
    [Fact]
    public void EliminateSpikes_WithCleanSignal_ReturnsSameSignal()
    {
        // Arrange
        var signal = new int[] { 100, 101, 102, 103, 104, 105 };

        // Act
        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikes(signal.ToArray());

        // Assert
        Assert.Equal(0, anomalyCount);
        for (int i = 0; i < signal.Length; i++)
        {
            Assert.Equal((ushort)signal[i], fixedSignal[i]);
        }
    }

    [Fact]
    public void EliminateSpikes_WithSinglePointSpike_EliminatesIt()
    {
        // Arrange
        var signal = new int[20];
        Array.Fill(signal, 100);
        signal[10] = 1000; // Spike

        // Act
        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikes(signal.ToArray());

        // Assert
        Assert.True(anomalyCount > 0);
        Assert.Equal((ushort)100, fixedSignal[10]);
    }

    [Fact]
    public void EliminateSpikes_WithInitialJump_CorrectsBaseline()
    {
        // Arrange
        var signal = new int[200];
        for (int i = 0; i < 10; i++) signal[i] = 100;
        for (int i = 10; i < 200; i++) signal[i] = 1100; // Jump of 1000

        // Act
        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikes(signal.ToArray());

        // Assert
        Assert.Equal((ushort)100, fixedSignal[10]);
        Assert.Equal((ushort)100, fixedSignal[199]);
    }

    [Fact]
    public void EliminateSpikes_WithDipAndJumpBack_CorrectsDip()
    {
        // Arrange
        var signal = new int[200];
        Array.Fill(signal, 1000);
        for (int i = 120; i < 170; i++) signal[i] = 200; 

        // Act
        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikes(signal.ToArray());

        // Assert
        Assert.Equal((ushort)1000, fixedSignal[130]);
        Assert.Equal((ushort)1000, fixedSignal[199]);
    }
}
