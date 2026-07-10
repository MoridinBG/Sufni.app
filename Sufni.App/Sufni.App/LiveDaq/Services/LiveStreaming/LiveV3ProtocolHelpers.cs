using System;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public static class LiveV3ProtocolHelpers
{
    public const byte AdmissionUnsupported = 1;
    public const byte AdmissionDisabled = 2;
    public const byte AdmissionUnavailable = 3;
    public const byte AdmissionCalibrationRequired = 4;
    public const byte AdmissionConflict = 5;
    public const byte AdmissionCapacity = 6;
    public const byte AdmissionInvalidRequest = 7;
    public const byte AdmissionNoTelemetry = 8;

    public static LiveStartErrorCode MapAdmissionReasonToStartErrorCode(LiveStartAdmissionReason? reason) =>
        reason?.Reason switch
        {
            AdmissionUnsupported or AdmissionDisabled or AdmissionInvalidRequest => LiveStartErrorCode.InvalidRequest,
            AdmissionConflict or AdmissionCapacity => LiveStartErrorCode.Busy,
            AdmissionUnavailable or AdmissionCalibrationRequired or AdmissionNoTelemetry => LiveStartErrorCode.NoSensorsStarted,
            _ => LiveStartErrorCode.InvalidRequest,
        };

    public static string CreateAdmissionMessage(LiveStartAdmissionReason? reason)
    {
        if (reason is not { } admissionReason)
        {
            return LiveStartErrorCode.InvalidRequest.UserMessage;
        }

        return admissionReason.Reason switch
        {
            AdmissionUnsupported => "The requested live stream, source, extension, or mode is unsupported.",
            AdmissionDisabled => "A requested live stream or source is disabled by device configuration.",
            AdmissionUnavailable => "Requested live telemetry is currently unavailable.",
            AdmissionCalibrationRequired => "A requested live sensor requires calibration.",
            AdmissionConflict => "Live preview conflicts with another active device service or priority owner.",
            AdmissionCapacity => "The device does not have enough live streaming capacity.",
            AdmissionInvalidRequest => "Live preview request was invalid.",
            AdmissionNoTelemetry => "The device reported no telemetry available for live preview.",
            _ => LiveStartErrorCode.InvalidRequest.UserMessage,
        };
    }

    public static string CreateTerminalSessionMessage(byte sessionResultReason) => sessionResultReason switch
    {
        1 => "Live preview ended before startup completed.",
        2 => "Live preview was stopped before startup completed.",
        4 => "Live preview stopped because the device entered a fault state.",
        5 => "Live preview stopped because telemetry was unavailable.",
        6 => "Live preview stopped because the device connection became unavailable.",
        _ => $"Live preview stopped with terminal reason {sessionResultReason}.",
    };

    public static string CreateErrorMessage(LiveV3Error error) =>
        $"LIVE v3 device error {error.Code}; offending frame type {error.OffendingFrameType}; detail {error.Detail}.";
}
