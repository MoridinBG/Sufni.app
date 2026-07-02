using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Presentation;

namespace Sufni.App.Sessions.Presentation;

public static class AnalysisSurfaceState
{
    private const string AnalysisWaitingMessage = "Waiting for analysis data.";
    private const string BalanceWaitingMessage = "Waiting for balance data.";
    private const string VibrationWaitingMessage = "Waiting for vibration data.";
    private const string AnalysisNoDataMessage = "No analysis data.";
    private const string RangeAnalysisNoDataMessage = "No analysis data for the selected range.";
    private const string BalanceNoDataMessage = "No balance analysis data.";
    private const string RangeBalanceNoDataMessage = "No balance analysis data for the selected range.";
    private const string VibrationNoDataMessage = "No vibration analysis data.";
    private const string RangeVibrationNoDataMessage = "No vibration analysis data for the selected range.";

    public static SurfacePresentationState ForSuspension(
        TelemetryData? telemetry,
        SuspensionType suspensionType,
        TelemetryTimeRange? range = null)
    {
        if (telemetry is null || !GetSuspension(telemetry, suspensionType).Present)
        {
            return SurfacePresentationState.Hidden;
        }

        return TelemetryStatistics.HasStrokeData(telemetry, suspensionType, range)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.NoData(SelectRangeAwareMessage(
                range,
                AnalysisNoDataMessage,
                RangeAnalysisNoDataMessage));
    }

    public static SurfacePresentationState ForSuspension(
        bool expected,
        TelemetryData? telemetry,
        SuspensionType suspensionType,
        TelemetryTimeRange? range = null)
    {
        if (!expected)
        {
            return SurfacePresentationState.Hidden;
        }

        if (telemetry is null || !GetSuspension(telemetry, suspensionType).Present)
        {
            return SurfacePresentationState.WaitingForData(AnalysisWaitingMessage);
        }

        return TelemetryStatistics.HasStrokeData(telemetry, suspensionType, range)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.WaitingForData(AnalysisWaitingMessage);
    }

    public static SurfacePresentationState ForBalance(
        TelemetryData? telemetry,
        BalanceType balanceType,
        TelemetryTimeRange? range = null)
    {
        if (telemetry is null || !telemetry.Front.Present || !telemetry.Rear.Present)
        {
            return SurfacePresentationState.Hidden;
        }

        return TelemetryStatistics.HasBalanceData(telemetry, balanceType, range)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.NoData(SelectRangeAwareMessage(
                range,
                BalanceNoDataMessage,
                RangeBalanceNoDataMessage));
    }

    public static SurfacePresentationState ForBalance(
        bool expected,
        TelemetryData? telemetry,
        BalanceType balanceType,
        TelemetryTimeRange? range = null)
    {
        if (!expected)
        {
            return SurfacePresentationState.Hidden;
        }

        if (telemetry is null || !telemetry.Front.Present || !telemetry.Rear.Present)
        {
            return SurfacePresentationState.WaitingForData(BalanceWaitingMessage);
        }

        return TelemetryStatistics.HasBalanceData(telemetry, balanceType, range)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.WaitingForData(BalanceWaitingMessage);
    }

    public static SurfacePresentationState ForVibration(
        TelemetryData? telemetry,
        SuspensionType suspensionType,
        ImuLocation location,
        TelemetryTimeRange? range = null)
    {
        if (telemetry is null)
        {
            return SurfacePresentationState.Hidden;
        }

        var suspension = GetSuspension(telemetry, suspensionType);
        if (!suspension.Present || !TelemetryStatistics.HasVibrationData(telemetry, location))
        {
            return SurfacePresentationState.Hidden;
        }

        return TelemetryStatistics.HasStrokeData(telemetry, suspensionType, range)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.NoData(SelectRangeAwareMessage(
                range,
                VibrationNoDataMessage,
                RangeVibrationNoDataMessage));
    }

    private static Suspension GetSuspension(TelemetryData telemetry, SuspensionType suspensionType)
    {
        return suspensionType == SuspensionType.Front ? telemetry.Front : telemetry.Rear;
    }

    private static string SelectRangeAwareMessage(
        TelemetryTimeRange? range,
        string fullSessionMessage,
        string selectedRangeMessage)
    {
        return range is null ? fullSessionMessage : selectedRangeMessage;
    }
}
