using Sufni.App.Bikes.Models;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.LiveDaq;

internal static class LiveCaptureTestData
{
    public static LiveDaqSessionContext Context(bool hasFrontTravelCalibration = true,
        bool hasRearTravelCalibration = true, Guid? bikeId = null) => new(
        IdentityKey: "board-1", BoardId: Guid.NewGuid(), DisplayName: "Board 1",
        SetupId: Guid.NewGuid(), SetupName: "race", BikeId: bikeId ?? Guid.NewGuid(), BikeName: "demo",
        BikeData: new BikeData(180, 170, measurement => measurement, measurement => measurement),
        TravelCalibration: new LiveDaqTravelCalibration(
            hasFrontTravelCalibration ? new LiveDaqTravelChannelCalibration(180, measurement => measurement) : null,
            hasRearTravelCalibration ? new LiveDaqTravelChannelCalibration(170, measurement => measurement) : null));

    public static LiveCaptureState State(
        bool canSave,
        TelemetryData? telemetryData = null,
        IReadOnlyList<TrackPoint>? trackPoints = null,
        LiveSessionHeader? header = null,
        long captureRevision = 1,
        LiveAnalysisOptions? options = null)
    {
        header ??= LiveProtocolTestFrames.CreateSessionHeaderModel();
        return new LiveCaptureState(
            Revision: captureRevision,
            CaptureRevision: canSave ? captureRevision : 0,
            Controls: new LiveCaptureControls(new LiveConnectionState.Connected(), null, header,
                header.SessionStartUtc, TimeSpan.FromSeconds(3), canSave),
            TrackPoints: trackPoints ?? [],
            SignalPresence: default,
            Analysis: telemetryData is null ? null : LiveSessionAnalysis.Compute(
                new LiveAnalysisKey(0, captureRevision, options ?? LiveAnalysisOptions.Default(DampingSpeedCutoffs.Default)),
                telemetryData, new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8)),
            AnalysisFailure: null);
    }

}
