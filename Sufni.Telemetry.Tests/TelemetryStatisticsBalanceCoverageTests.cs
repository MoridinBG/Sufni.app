using static Sufni.Telemetry.Tests.SyntheticSensorRecording;

namespace Sufni.Telemetry.Tests;

public class TelemetryStatisticsBalanceCoverageTests
{
    // Both sides ride for 60 s; the rear is dropped from 20 s to 30 s.
    private static readonly TelemetryData RearDropped =
        Derive(Recording(60, Riding, Riding), 0, (new SensorIdentity(SensorKind.ShockTravel), 20, 30));

    private static TelemetryTimeSelection Selection(double start, double end) => new([new TelemetryTimeRange(start, end)]);

    [Fact]
    public void Balance_IsAvailable_ForASelectionInsideTheTimeBothSidesHaveData()
    {
        Assert.True(TelemetryStatistics.HasBalanceData(RearDropped, BalanceType.Compression, Selection(35, 55)));
        Assert.NotEmpty(TelemetryStatistics.CalculateBalance(RearDropped, BalanceType.Compression, Selection(35, 55)).FrontTravel);
    }

    [Fact]
    public void Balance_IsUnavailable_ForASelectionPartlyWithoutOneSide()
    {
        Assert.False(TelemetryStatistics.HasBalanceData(RearDropped, BalanceType.Compression, Selection(15, 40)));
        Assert.Empty(TelemetryStatistics.CalculateBalance(RearDropped, BalanceType.Compression, Selection(15, 40)).FrontTravel);
    }

    [Fact]
    public void Balance_IsUnavailable_WithoutASelection_WhenOneSideMissesPartOfTheSession()
    {
        Assert.False(TelemetryStatistics.HasBalanceData(RearDropped, BalanceType.Compression));
        Assert.Empty(TelemetryStatistics.CalculateBalance(RearDropped, BalanceType.Compression).RearTravel);
    }

    [Fact]
    public void Balance_IsAvailable_WithoutASelection_WhenBothSidesCoverTheSession()
    {
        Assert.True(TelemetryStatistics.HasBalanceData(Derive(Recording(60, Riding, Riding), 0), BalanceType.Compression));
    }
}
