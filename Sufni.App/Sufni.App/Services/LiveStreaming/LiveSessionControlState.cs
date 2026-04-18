using System;

namespace Sufni.App.Services.LiveStreaming;

public sealed record LiveSessionControlState(
    LiveConnectionState ConnectionState,
    string? LastError,
    LiveSessionHeader? SessionHeader,
    DateTimeOffset? CaptureStartUtc,
    TimeSpan CaptureDuration,
    uint TravelQueueDepth,
    uint ImuQueueDepth,
    uint GpsQueueDepth,
    uint TravelDroppedBatches,
    uint ImuDroppedBatches,
    uint GpsDroppedBatches,
    bool CanSave)
{
    public string ConnectionStateText => $"State: {ConnectionState}";
    public string CaptureDurationText => $"Capture: {CaptureDuration:g}";
    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);
    public bool HasSessionHeader => SessionHeader is not null;
    public string SessionIdText => SessionHeader is null ? "Session: -" : $"Session: {SessionHeader.SessionId}";
    public string AcceptedTravelRateText => SessionHeader is null ? "Travel: -" : $"Travel: {SessionHeader.AcceptedTravelHz} Hz";
    public string AcceptedImuRateText => SessionHeader is null ? "IMU: -" : $"IMU: {SessionHeader.AcceptedImuHz} Hz";
    public string AcceptedGpsRateText => SessionHeader is null ? "GPS: -" : $"GPS: {SessionHeader.AcceptedGpsFixHz} Hz";

    public static readonly LiveSessionControlState Empty = new(
        ConnectionState: LiveConnectionState.Disconnected,
        LastError: null,
        SessionHeader: null,
        CaptureStartUtc: null,
        CaptureDuration: TimeSpan.Zero,
        TravelQueueDepth: 0,
        ImuQueueDepth: 0,
        GpsQueueDepth: 0,
        TravelDroppedBatches: 0,
        ImuDroppedBatches: 0,
        GpsDroppedBatches: 0,
        CanSave: false);
}