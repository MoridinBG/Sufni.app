using System;
using System.Collections.Generic;

namespace Sufni.App.Services.LiveStreaming;

public enum LiveConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
}

public sealed record LiveSessionContractSnapshot(
    uint? SessionId,
    LiveSensorMask SelectedSensorMask,
    uint? PublishCadenceMs,
    uint? AcceptedTravelHz,
    uint? AcceptedImuHz,
    uint? AcceptedGpsFixHz,
    uint? TravelPeriodUs,
    uint? ImuPeriodUs,
    uint? GpsFixIntervalMs,
    DateTimeOffset? SessionStartUtc,
    uint? TravelQueueCapacity,
    uint? ImuQueueCapacity,
    uint? GpsQueueCapacity,
    LiveSessionFlags Flags,
    IReadOnlyList<LiveImuLocation> ActiveImuLocations)
{
    public static readonly LiveSessionContractSnapshot Empty = new(
        SessionId: null,
        SelectedSensorMask: LiveSensorMask.None,
        PublishCadenceMs: null,
        AcceptedTravelHz: null,
        AcceptedImuHz: null,
        AcceptedGpsFixHz: null,
        TravelPeriodUs: null,
        ImuPeriodUs: null,
        GpsFixIntervalMs: null,
        SessionStartUtc: null,
        TravelQueueCapacity: null,
        ImuQueueCapacity: null,
        GpsQueueCapacity: null,
        Flags: LiveSessionFlags.None,
        ActiveImuLocations: []);
}

public sealed record LiveTravelUiSnapshot(
    bool IsActive,
    bool HasData,
    double? FrontTravel,
    double? RearTravel,
    TimeSpan? SampleOffset,
    uint QueueDepth,
    uint DroppedBatches)
{
    public static readonly LiveTravelUiSnapshot Empty = new(
        IsActive: false,
        HasData: false,
        FrontTravel: null,
        RearTravel: null,
        SampleOffset: null,
        QueueDepth: 0,
        DroppedBatches: 0);
}

public sealed record LiveImuUiSnapshot(
    LiveImuLocation Location,
    bool HasData,
    short? Ax,
    short? Ay,
    short? Az,
    short? Gx,
    short? Gy,
    short? Gz,
    TimeSpan? SampleOffset,
    uint QueueDepth,
    uint DroppedBatches);

public sealed record LiveGpsUiSnapshot(
    bool IsActive,
    bool HasData,
    GpsPreviewState PreviewState,
    DateTime? FixTimestampUtc,
    double? Latitude,
    double? Longitude,
    float? Altitude,
    float? Speed,
    float? Heading,
    byte? Satellites,
    float? Epe2d,
    float? Epe3d,
    uint QueueDepth,
    uint DroppedBatches)
{
    public static readonly LiveGpsUiSnapshot Empty = new(
        IsActive: false,
        HasData: false,
        PreviewState: GpsPreviewState.NoFix,
        FixTimestampUtc: null,
        Latitude: null,
        Longitude: null,
        Altitude: null,
        Speed: null,
        Heading: null,
        Satellites: null,
        Epe2d: null,
        Epe3d: null,
        QueueDepth: 0,
        DroppedBatches: 0);
}

public sealed record LiveDaqUiSnapshot(
    LiveConnectionState ConnectionState,
    string ConnectionStateText,
    string? LastError,
    DateTimeOffset? LastFrameReceivedUtc,
    LiveSessionContractSnapshot Session,
    LiveTravelUiSnapshot Travel,
    IReadOnlyList<LiveImuUiSnapshot> Imus,
    LiveGpsUiSnapshot Gps)
{
    public static readonly LiveDaqUiSnapshot Empty = new(
        ConnectionState: LiveConnectionState.Disconnected,
        ConnectionStateText: ToConnectionStateText(LiveConnectionState.Disconnected),
        LastError: null,
        LastFrameReceivedUtc: null,
        Session: LiveSessionContractSnapshot.Empty,
        Travel: LiveTravelUiSnapshot.Empty,
        Imus: [],
        Gps: LiveGpsUiSnapshot.Empty);

    public bool HasAcceptedSession => Session.SessionId is not null;
    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);
    public bool HasImuData => Imus.Count > 0;
    public bool HasTravelData => Travel.HasData;
    public bool HasGpsData => Gps.HasData;

    public static string ToConnectionStateText(LiveConnectionState state) => state switch
    {
        LiveConnectionState.Disconnected => "Disconnected",
        LiveConnectionState.Connecting => "Connecting",
        LiveConnectionState.Connected => "Connected",
        LiveConnectionState.Disconnecting => "Disconnecting",
        _ => state.ToString(),
    };
}