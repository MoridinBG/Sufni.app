using System;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.SessionDetails;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

public interface IRecordedSessionGraphWorkspace
{
    TelemetryData? TelemetryData { get; }
    SessionTimelineLinkViewModel Timeline { get; }
}

public interface ISessionMediaWorkspace
{
    MapViewModel? MapViewModel { get; }
    bool HasSessionTrackPoints { get; }
    SessionTimelineLinkViewModel Timeline { get; }
    double? MapVideoWidth { get; }
    string? VideoUrl { get; }
}

public interface ISessionStatisticsWorkspace
{
    TelemetryData? TelemetryData { get; }
    bool HasFrontStatistics { get; }
    bool HasRearStatistics { get; }
    bool HasCompressionBalanceTelemetry { get; }
    bool HasReboundBalanceTelemetry { get; }
    SessionDamperPercentages DamperPercentages { get; }
}

public interface ISessionSidebarWorkspace
{
    string? Name { get; set; }
    string? DescriptionText { get; set; }
    SuspensionSettings ForkSettings { get; }
    SuspensionSettings ShockSettings { get; }
    IAsyncRelayCommand SaveCommand { get; }
    IAsyncRelayCommand ResetCommand { get; }
}

public interface ILiveSessionGraphWorkspace
{
    IObservable<LiveGraphBatch> GraphBatches { get; }
    LiveSessionPlotRanges PlotRanges { get; }
    SessionTimelineLinkViewModel Timeline { get; }
}

public sealed record LiveSessionPlotRanges(
    double TravelMaximum,
    double VelocityMaximum,
    double ImuMaximum)
{
    public double VelocityMinimum => -VelocityMaximum;

    public static readonly LiveSessionPlotRanges Default = new(
        TravelMaximum: 1,
        VelocityMaximum: 5,
        ImuMaximum: 5);
}

public interface ILiveSessionControlsWorkspace
{
    LiveSessionControlState ControlState { get; }
    IAsyncRelayCommand SaveCommand { get; }
    IAsyncRelayCommand ResetCommand { get; }
}

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