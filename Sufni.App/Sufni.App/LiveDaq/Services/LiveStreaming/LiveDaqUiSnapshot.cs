using System;
using System.Collections.Generic;

using Sufni.App.Shared.Formatting;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed record LiveSessionContractSnapshot(
    uint? SessionId,
    LiveSensorInstanceMask RequestedSensorMask,
    LiveSensorInstanceMask AcceptedSensorMask,
    uint? AcceptedTravelHz,
    uint? AcceptedImuHz,
    uint? AcceptedGpsFixHz,
    DateTimeOffset? SessionStartUtc,
    IReadOnlyList<LiveImuLocation> ActiveImuLocations,
    uint? AcceptedTravelRateMhz = null,
    uint? AcceptedImuRateMhz = null,
    uint? AcceptedGpsRateMhz = null)
{
    public static readonly LiveSessionContractSnapshot Empty = new(
        SessionId: null,
        RequestedSensorMask: LiveSensorInstanceMask.None,
        AcceptedSensorMask: LiveSensorInstanceMask.None,
        AcceptedTravelHz: null,
        AcceptedImuHz: null,
        AcceptedGpsFixHz: null,
        SessionStartUtc: null,
        ActiveImuLocations: [],
        AcceptedTravelRateMhz: null,
        AcceptedImuRateMhz: null,
        AcceptedGpsRateMhz: null);

    public string AcceptedTravelRateText => LiveProtocolHelpers.FormatRateText("Travel", AcceptedTravelRateMhz);

    public string AcceptedImuRateText => LiveProtocolHelpers.FormatRateText("IMU", AcceptedImuRateMhz);

    public string AcceptedGpsRateText => LiveProtocolHelpers.FormatRateText("GPS", AcceptedGpsRateMhz);
}

public sealed record LiveTravelUiSnapshot(
    bool FrontIsActive,
    bool RearIsActive,
    bool HasData,
    ushort? FrontMeasurement,
    ushort? RearMeasurement,
    TimeSpan? SampleDelay,
    uint QueueDepth)
{
    public string SampleDelayText => SampleDelay is { } delay
        ? $"Sample delay: {delay.TotalMilliseconds:F0} ms"
        : "";

    public static readonly LiveTravelUiSnapshot Empty = new(
        FrontIsActive: false,
        RearIsActive: false,
        HasData: false,
        FrontMeasurement: null,
        RearMeasurement: null,
        SampleDelay: null,
        QueueDepth: 0);
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
    TimeSpan? SampleDelay,
    uint QueueDepth)
{
    public string SampleDelayText => SampleDelay is { } delay
        ? $"Sample delay: {delay.TotalMilliseconds:F0} ms"
        : "";
}

public sealed record LiveGpsUiSnapshot(
    bool HasData,
    GpsPreviewState PreviewState,
    DateTime? FixTimestampUtc,
    double? Latitude,
    double? Longitude,
    uint QueueDepth)
{
    public static readonly LiveGpsUiSnapshot Empty = new(
        HasData: false,
        PreviewState: GpsPreviewState.NoFix,
        FixTimestampUtc: null,
        Latitude: null,
        Longitude: null,
        QueueDepth: 0);
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
        ConnectionState: new LiveConnectionState.Disconnected(),
        ConnectionStateText: ToConnectionStateText(new LiveConnectionState.Disconnected()),
        LastError: null,
        LastFrameReceivedUtc: null,
        Session: LiveSessionContractSnapshot.Empty,
        Travel: LiveTravelUiSnapshot.Empty,
        Imus: [],
        Gps: LiveGpsUiSnapshot.Empty);

    public bool HasAcceptedSession => Session.SessionId is not null;
    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);
    public bool HasImuData => Imus.Count > 0;

    public string SessionLengthText =>
        LastFrameReceivedUtc is { } lastFrame && Session.SessionStartUtc is { } start
            ? $"Session length: {UnitsFormatter.FormatDuration(lastFrame - start)}"
            : "";

    public static string ToConnectionStateText(LiveConnectionState state) => state switch
    {
        LiveConnectionState.Disconnected => "Disconnected",
        LiveConnectionState.Connecting => "Connecting",
        LiveConnectionState.Connected => "Connected",
        LiveConnectionState.Disconnecting => "Disconnecting",
        LiveConnectionState.Closed => "Closed",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
