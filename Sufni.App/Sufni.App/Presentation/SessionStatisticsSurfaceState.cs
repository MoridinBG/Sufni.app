using Sufni.Telemetry;

namespace Sufni.App.Presentation;

public static class SessionStatisticsSurfaceState
{
    private const string StatisticsWaitingMessage = "Waiting for statistics.";
    private const string BalanceWaitingMessage = "Waiting for balance data.";
    private const string VibrationWaitingMessage = "Waiting for vibration data.";

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
            : SurfacePresentationState.WaitingForData(StatisticsWaitingMessage);
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
            return SurfacePresentationState.WaitingForData(StatisticsWaitingMessage);
        }

        return TelemetryStatistics.HasStrokeData(telemetry, suspensionType, range)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.WaitingForData(StatisticsWaitingMessage);
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
            : SurfacePresentationState.WaitingForData(BalanceWaitingMessage);
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
            : SurfacePresentationState.WaitingForData(VibrationWaitingMessage);
    }

    private static Suspension GetSuspension(TelemetryData telemetry, SuspensionType suspensionType)
    {
        return suspensionType == SuspensionType.Front ? telemetry.Front : telemetry.Rear;
    }
}
