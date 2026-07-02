using System;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

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
    public LiveDaqClientDropCounters ClientDropCounters { get; init; } = LiveDaqClientDropCounters.Empty;

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
        TravelQueueDepth: 0,
        ImuQueueDepth: 0,
        GpsQueueDepth: 0,
        TravelDroppedBatches: 0,
        ImuDroppedBatches: 0,
        GpsDroppedBatches: 0,
        CanSave: false);

}
