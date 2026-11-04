using System.Collections.Generic;
using Sufni.Telemetry;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;

using Sufni.App.LiveDaq.Queries;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public enum LiveSignalBatchKind { Signals, Reset }

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
    public LiveSignalBatchKind Kind { get; init; }

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

public sealed record LiveSessionCapturePackage(
    LiveDaqSessionContext Context,
    LiveTelemetryCapture TelemetryCapture)
{
    public long CaptureRevision { get; init; }
}
