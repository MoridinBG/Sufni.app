using System;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public static class LiveV3ProtocolHelpers
{
    public const byte AdmissionInvalidShape = 1;
    public const byte AdmissionUnknownId = 2;
    public const byte AdmissionUnsupportedFlags = 3;
    public const byte AdmissionCapacity = 4;
    public const byte AdmissionPriorityConflict = 5;
    public const byte AdmissionActiveOwner = 6;
    public const byte AdmissionBusy = 7;
    public const byte AdmissionNoTelemetry = 8;
    public const byte AdmissionAllRequestedStreamsOmitted = 9;

    public static LiveStartErrorCode MapAdmissionReasonToStartErrorCode(LiveStartAdmissionReason? reason) =>
        reason?.Reason switch
        {
            AdmissionInvalidShape or AdmissionUnknownId or AdmissionUnsupportedFlags => LiveStartErrorCode.InvalidRequest,
            AdmissionCapacity or AdmissionPriorityConflict or AdmissionActiveOwner or AdmissionBusy => LiveStartErrorCode.Busy,
            AdmissionNoTelemetry or AdmissionAllRequestedStreamsOmitted => LiveStartErrorCode.NoSensorsStarted,
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
            AdmissionInvalidShape => "Live preview request was invalid.",
            AdmissionUnknownId => "The device rejected an unknown live stream or source.",
            AdmissionUnsupportedFlags => "The device rejected unsupported live preview flags.",
            AdmissionCapacity => "The device does not have enough live streaming capacity.",
            AdmissionPriorityConflict => "Live preview is blocked by a higher priority stream.",
            AdmissionActiveOwner => "Another live preview owner is already active.",
            AdmissionBusy => LiveStartErrorCode.Busy.UserMessage,
            AdmissionNoTelemetry => "The device reported no telemetry available for live preview.",
            AdmissionAllRequestedStreamsOmitted => LiveStartErrorCode.NoSensorsStarted.UserMessage,
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

    public static string CreateErrorMessage(byte errorCode) =>
        $"LIVE v3 device error {errorCode}.";
}

