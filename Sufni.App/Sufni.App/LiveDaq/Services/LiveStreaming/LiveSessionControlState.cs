using System;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed record LiveSessionControlState(
    LiveConnectionState ConnectionState,
    string? LastError,
    LiveSessionHeader? SessionHeader,
    DateTimeOffset? CaptureStartUtc,
    TimeSpan CaptureDuration,
    bool CanSave)
{
    public string ConnectionStateText => $"State: {ConnectionState}";
    public string CaptureDurationText => $"Capture: {CaptureDuration:g}";
    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);
    public bool HasSessionHeader => SessionHeader is not null;
    public string SessionIdText => SessionHeader is null ? "Session: -" : $"Session: {SessionHeader.SessionId}";
    public string AcceptedTravelRateText => LiveProtocolHelpers.FormatRateText("Travel", SessionHeader?.AcceptedTravelRateMhz);
    public string AcceptedImuRateText => LiveProtocolHelpers.FormatRateText("IMU", SessionHeader?.AcceptedImuRateMhz);
    public string AcceptedGpsRateText => LiveProtocolHelpers.FormatRateText("GPS", SessionHeader?.AcceptedGpsRateMhz);

    public static readonly LiveSessionControlState Empty = new(
        ConnectionState: LiveConnectionState.Disconnected,
        LastError: null,
        SessionHeader: null,
        CaptureStartUtc: null,
        CaptureDuration: TimeSpan.Zero,
        CanSave: false);

}
