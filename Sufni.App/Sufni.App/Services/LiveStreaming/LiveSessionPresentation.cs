using System;
using System.Collections.Generic;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.Telemetry;

namespace Sufni.App.Services.LiveStreaming;

public sealed record LiveGraphBatch(
    long Revision,
    IReadOnlyList<double> TravelTimes,
    IReadOnlyList<double> FrontTravel,
    IReadOnlyList<double> RearTravel,
    IReadOnlyList<double> VelocityTimes,
    IReadOnlyList<double> FrontVelocity,
    IReadOnlyList<double> RearVelocity,
    IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> ImuTimes,
    IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> ImuMagnitudes)
{
    public static readonly LiveGraphBatch Empty = new(
        Revision: 0,
        TravelTimes: [],
        FrontTravel: [],
        RearTravel: [],
        VelocityTimes: [],
        FrontVelocity: [],
        RearVelocity: [],
        ImuTimes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>(),
        ImuMagnitudes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>());
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
    TelemetryData? StatisticsTelemetry,
    SessionDamperPercentages DamperPercentages,
    IReadOnlyList<TrackPoint> SessionTrackPoints,
    LiveSessionControlState Controls,
    long CaptureRevision)
{
    public static readonly LiveSessionPresentationSnapshot Empty = new(
        Stream: new LiveSessionStreamPresentation.Idle(),
        StatisticsTelemetry: null,
        DamperPercentages: new SessionDamperPercentages(null, null, null, null, null, null, null, null),
        SessionTrackPoints: [],
        Controls: LiveSessionControlState.Empty,
        CaptureRevision: 0);
}

public sealed record LiveSessionCapturePackage(
    LiveDaqSessionContext Context,
    LiveTelemetryCapture TelemetryCapture);