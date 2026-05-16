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
        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikes(signal.ToArray(), sampleRate: 1000);

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
        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikes(signal.ToArray(), sampleRate: 1000);

        // Assert
        Assert.True(anomalyCount > 0);
        Assert.Equal((ushort)100, fixedSignal[10]);
    }

    [Fact]
    public void EliminateSpikes_WithPositiveSpikeWindow_DoesNotShiftRecoveredTail()
    {
        var signal = new int[240];
        Array.Fill(signal, 60);
        for (var index = 120; index < 125; index++)
        {
            signal[index] = 4095;
        }

        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikes(signal.ToArray(), sampleRate: 1000);

        Assert.True(anomalyCount > 0);
        Assert.Equal((ushort)60, fixedSignal[130]);
        Assert.Equal((ushort)60, fixedSignal[239]);
    }

    [Fact]
    public void EliminateSpikesAsInt_WithContinuingPositiveRamp_DoesNotFlattenRamp()
    {
        int[] signal = [12, 12, 12, 15, 46, 109, 181, 238, 275, 294, 299, 299, 302, 316, 336, 352];

        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikesAsInt(signal.ToArray(), sampleRate: 1000);

        Assert.Equal(0, anomalyCount);
        Assert.Equal(signal, fixedSignal);
    }

    [Theory]
    [InlineData(50, 400)]
    [InlineData(100, 200)]
    [InlineData(200, 100)]
    public void EliminateSpikesAsInt_WithLowerSampleRateRampBelowStepRate_DoesNotFlattenRamp(
        int sampleRate,
        int step)
    {
        int[] signal = [0, step, step * 2, step * 3, step * 3, step * 3];

        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikesAsInt(signal.ToArray(), sampleRate);

        Assert.Equal(0, anomalyCount);
        Assert.Equal(signal, fixedSignal);
    }

    [Fact]
    public void EliminateSpikesAsInt_WithContinuingNegativeRamp_DoesNotFlattenRamp()
    {
        var signal = new int[140];
        Array.Fill(signal, 300);
        int[] ramp = [262, 230, 193, 153, 113, 71, 30, -12, -43, -70, -100, -130, -160, -190, -215, -235, -250, -260];
        ramp.CopyTo(signal, 120);
        for (var index = 120 + ramp.Length; index < signal.Length; index++)
        {
            signal[index] = ramp[^1];
        }

        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikesAsInt(signal.ToArray(), sampleRate: 1000);

        Assert.Equal(0, anomalyCount);
        Assert.Equal(signal, fixedSignal);
    }

    [Fact]
    public void EliminateSpikesAsInt_WithPlateauingMultiSampleJump_StillFlattensJump()
    {
        var signal = new int[140];
        Array.Fill(signal, 10);
        int[] jump = [40, 80, 130, 170, 171, 168, 172, 171];
        jump.CopyTo(signal, 110);

        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikesAsInt(signal.ToArray(), sampleRate: 1000);

        Assert.True(anomalyCount > 0);
        Assert.Equal(170, fixedSignal[110]);
        Assert.Equal(170, fixedSignal[111]);
        Assert.Equal(170, fixedSignal[112]);
        Assert.Equal(170, fixedSignal[113]);
    }

    [Fact]
    public void EliminateSpikes_WithInitialJump_CorrectsBaseline()
    {
        // Arrange
        var signal = new int[200];
        for (int i = 0; i < 10; i++) signal[i] = 100;
        for (int i = 10; i < 200; i++) signal[i] = 1100; // Jump of 1000

        // Act
        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikes(signal.ToArray(), sampleRate: 1000);

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
        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikes(signal.ToArray(), sampleRate: 1000);

        // Assert
        Assert.Equal((ushort)1000, fixedSignal[130]);
        Assert.Equal((ushort)1000, fixedSignal[199]);
    }

    [Fact]
    public void EliminateSpikes_WithUnresolvedDip_CorrectsSignalToEnd()
    {
        var signal = new int[200];
        Array.Fill(signal, 1000);
        for (int i = 120; i < 200; i++)
        {
            signal[i] = 200;
        }

        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikes(signal.ToArray(), sampleRate: 1000);

        Assert.True(anomalyCount > 0);
        Assert.Equal((ushort)1000, fixedSignal[130]);
        Assert.Equal((ushort)1000, fixedSignal[199]);
    }

    [Fact]
    public void EliminateSpikes_WithSuccessiveNegativeChanges_WaitsForActualRecovery()
    {
        var signal = new int[200];
        Array.Fill(signal, 1000);
        for (int i = 120; i < 140; i++)
        {
            signal[i] = 700;
        }
        for (int i = 140; i < 170; i++)
        {
            signal[i] = 500;
        }

        var (fixedSignal, anomalyCount) = SpikeElimination.EliminateSpikes(signal.ToArray(), sampleRate: 1000);

        Assert.True(anomalyCount > 0);
        Assert.Equal((ushort)1000, fixedSignal[130]);
        Assert.Equal((ushort)1000, fixedSignal[150]);
        Assert.Equal((ushort)1000, fixedSignal[199]);
    }
}
