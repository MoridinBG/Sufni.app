using System;
using System.Collections.Generic;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;

using Sufni.App.LiveDaq.Queries;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed record LiveSignalBatch(
    long Revision,
    IReadOnlyList<double> TravelTimes,
    IReadOnlyList<double> FrontTravel,
    IReadOnlyList<double> RearTravel,
    IReadOnlyList<double> VelocityTimes,
    IReadOnlyList<double> FrontVelocity,
    IReadOnlyList<double> RearVelocity,
    IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> ImuTimes,
    IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> ImuVibrationRms,
    IReadOnlyList<double> FramePitchRollTimes,
    IReadOnlyList<double> FramePitchDegrees,
    IReadOnlyList<double> FrameRollDegrees)
{
    public static readonly LiveSignalBatch Empty = new(
        Revision: 0,
        TravelTimes: [],
        FrontTravel: [],
        RearTravel: [],
        VelocityTimes: [],
        FrontVelocity: [],
        RearVelocity: [],
        ImuTimes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>(),
        ImuVibrationRms: new Dictionary<LiveImuLocation, IReadOnlyList<double>>(),
        FramePitchRollTimes: [],
        FramePitchDegrees: [],
        FrameRollDegrees: []);
}

public abstract record LiveSessionStreamPresentation
{
    private LiveSessionStreamPresentation() { }

    public sealed record Idle : LiveSessionStreamPresentation;
    public sealed record Connecting : LiveSessionStreamPresentation;
    public sealed record Streaming(DateTime? SessionStartLocalTime, LiveSessionHeader SessionHeader) : LiveSessionStreamPresentation;
    public sealed record Closed(string? ErrorMessage) : LiveSessionStreamPresentation;
}

public sealed record LiveSessionPresentationSnapshot(
    LiveSessionStreamPresentation Stream,
    TelemetryData? AnalysisTelemetry,
    SessionDampingPercentages DampingPercentages,
    IReadOnlyList<TrackPoint> SessionTrackPoints,
    LiveSessionControlState Controls,
    long CaptureRevision)
{
    public static readonly LiveSessionPresentationSnapshot Empty = new(
        Stream: new LiveSessionStreamPresentation.Idle(),
        AnalysisTelemetry: null,
        DampingPercentages: SessionDampingPercentages.Empty,
        SessionTrackPoints: [],
        Controls: LiveSessionControlState.Empty,
        CaptureRevision: 0);
}

public sealed record LiveSessionCapturePackage(
    LiveDaqSessionContext Context,
    LiveTelemetryCapture TelemetryCapture);
