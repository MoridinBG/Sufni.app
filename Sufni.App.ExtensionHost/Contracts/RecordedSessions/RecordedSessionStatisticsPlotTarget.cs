using System;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public enum RecordedSessionStatisticsPlotFamily
{
    TravelHistogram,
    TravelFrequencyHistogram,
    VelocityHistogram,
    Balance,
    StrokeLengthHistogram,
    StrokeSpeedHistogram,
    DeepTravelHistogram,
    VibrationThirds,
    SessionAnalysis,
}

public readonly record struct RecordedSessionStatisticsPlotTarget
{
    private RecordedSessionStatisticsPlotTarget(
        RecordedSessionStatisticsPlotFamily family,
        SuspensionType? suspensionType,
        BalanceType? balanceType,
        ImuLocation? imuLocation)
    {
        Family = family;
        SuspensionType = suspensionType;
        BalanceType = balanceType;
        ImuLocation = imuLocation;
    }

    public RecordedSessionStatisticsPlotFamily Family { get; }
    public SuspensionType? SuspensionType { get; }
    public BalanceType? BalanceType { get; }
    public ImuLocation? ImuLocation { get; }

    public static RecordedSessionStatisticsPlotTarget TravelHistogram(SuspensionType suspensionType) =>
        new(RecordedSessionStatisticsPlotFamily.TravelHistogram, suspensionType, null, null);

    public static RecordedSessionStatisticsPlotTarget TravelFrequencyHistogram(SuspensionType suspensionType) =>
        new(RecordedSessionStatisticsPlotFamily.TravelFrequencyHistogram, suspensionType, null, null);

    public static RecordedSessionStatisticsPlotTarget VelocityHistogram(SuspensionType suspensionType) =>
        new(RecordedSessionStatisticsPlotFamily.VelocityHistogram, suspensionType, null, null);

    public static RecordedSessionStatisticsPlotTarget Balance(BalanceType balanceType) =>
        new(RecordedSessionStatisticsPlotFamily.Balance, null, balanceType, null);

    public static RecordedSessionStatisticsPlotTarget StrokeLengthHistogram(
        SuspensionType suspensionType,
        BalanceType balanceType) =>
        new(RecordedSessionStatisticsPlotFamily.StrokeLengthHistogram, suspensionType, balanceType, null);

    public static RecordedSessionStatisticsPlotTarget StrokeSpeedHistogram(
        SuspensionType suspensionType,
        BalanceType balanceType) =>
        new(RecordedSessionStatisticsPlotFamily.StrokeSpeedHistogram, suspensionType, balanceType, null);

    public static RecordedSessionStatisticsPlotTarget DeepTravelHistogram(SuspensionType suspensionType) =>
        new(RecordedSessionStatisticsPlotFamily.DeepTravelHistogram, suspensionType, null, null);

    public static RecordedSessionStatisticsPlotTarget VibrationThirds(
        SuspensionType suspensionType,
        ImuLocation imuLocation) =>
        new(RecordedSessionStatisticsPlotFamily.VibrationThirds, suspensionType, null, imuLocation);

    public static RecordedSessionStatisticsPlotTarget SessionAnalysis() =>
        new(RecordedSessionStatisticsPlotFamily.SessionAnalysis, null, null, null);
}
