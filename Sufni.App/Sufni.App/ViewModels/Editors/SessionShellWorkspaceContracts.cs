using System;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

public interface IRecordedSessionGraphWorkspace
{
    TelemetryData? TelemetryData { get; }
    SurfacePresentationState TravelGraphState { get; }
    SurfacePresentationState ImuGraphState { get; }
    SessionTimelineLinkViewModel Timeline { get; }
}

public interface ISessionMediaWorkspace
{
    bool HasMediaContent { get; }
    MapViewModel? MapViewModel { get; }
    SurfacePresentationState MapState { get; }
    SurfacePresentationState VideoState { get; }
    SessionTimelineLinkViewModel Timeline { get; }
    double? MapVideoWidth { get; }
    string? VideoUrl { get; }
}

public interface ISessionStatisticsWorkspace
{
    TelemetryData? TelemetryData { get; }
    SurfacePresentationState FrontStatisticsState { get; }
    SurfacePresentationState RearStatisticsState { get; }
    SurfacePresentationState CompressionBalanceState { get; }
    SurfacePresentationState ReboundBalanceState { get; }
    SurfacePresentationState FrontForkVibrationState { get; }
    SurfacePresentationState FrontFrameVibrationState { get; }
    SurfacePresentationState RearForkVibrationState { get; }
    SurfacePresentationState RearFrameVibrationState { get; }
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
    SurfacePresentationState TravelGraphState { get; }
    SurfacePresentationState ImuGraphState { get; }
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