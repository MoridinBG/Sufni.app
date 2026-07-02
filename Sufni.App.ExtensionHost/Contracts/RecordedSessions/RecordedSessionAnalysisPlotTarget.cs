using System;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public enum RecordedSessionAnalysisPlotFamily
{
    TravelDistribution,
    TravelFrequencyDistribution,
    VelocityDistribution,
    Balance,
    StrokeLengthDistribution,
    StrokeSpeedDistribution,
    DeepTravelDistribution,
    VibrationDistribution,
    SessionAnalysis,
}

public readonly record struct RecordedSessionAnalysisPlotTarget
{
    private RecordedSessionAnalysisPlotTarget(
        RecordedSessionAnalysisPlotFamily family,
        SuspensionType? suspensionType,
        BalanceType? balanceType,
        ImuLocation? imuLocation)
    {
        Family = family;
        SuspensionType = suspensionType;
        BalanceType = balanceType;
        ImuLocation = imuLocation;
    }

    public RecordedSessionAnalysisPlotFamily Family { get; }
    public SuspensionType? SuspensionType { get; }
    public BalanceType? BalanceType { get; }
    public ImuLocation? ImuLocation { get; }

    public static RecordedSessionAnalysisPlotTarget TravelDistribution(SuspensionType suspensionType) =>
        new(RecordedSessionAnalysisPlotFamily.TravelDistribution, suspensionType, null, null);

    public static RecordedSessionAnalysisPlotTarget TravelFrequencyDistribution(SuspensionType suspensionType) =>
        new(RecordedSessionAnalysisPlotFamily.TravelFrequencyDistribution, suspensionType, null, null);

    public static RecordedSessionAnalysisPlotTarget VelocityDistribution(SuspensionType suspensionType) =>
        new(RecordedSessionAnalysisPlotFamily.VelocityDistribution, suspensionType, null, null);

    public static RecordedSessionAnalysisPlotTarget Balance(BalanceType balanceType) =>
        new(RecordedSessionAnalysisPlotFamily.Balance, null, balanceType, null);

    public static RecordedSessionAnalysisPlotTarget StrokeLengthDistribution(
        SuspensionType suspensionType,
        BalanceType balanceType) =>
        new(RecordedSessionAnalysisPlotFamily.StrokeLengthDistribution, suspensionType, balanceType, null);

    public static RecordedSessionAnalysisPlotTarget StrokeSpeedDistribution(
        SuspensionType suspensionType,
        BalanceType balanceType) =>
        new(RecordedSessionAnalysisPlotFamily.StrokeSpeedDistribution, suspensionType, balanceType, null);

    public static RecordedSessionAnalysisPlotTarget DeepTravelDistribution(SuspensionType suspensionType) =>
        new(RecordedSessionAnalysisPlotFamily.DeepTravelDistribution, suspensionType, null, null);

    public static RecordedSessionAnalysisPlotTarget VibrationDistribution(
        SuspensionType suspensionType,
        ImuLocation imuLocation) =>
        new(RecordedSessionAnalysisPlotFamily.VibrationDistribution, suspensionType, null, imuLocation);

    public static RecordedSessionAnalysisPlotTarget SessionAnalysis() =>
        new(RecordedSessionAnalysisPlotFamily.SessionAnalysis, null, null, null);
}
