using System.Collections.Generic;
using System.Linq;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal static class RecordedSessionPresentationDeriver
{
    public static bool HasTravelTelemetry(TelemetryData? telemetry)
    {
        return telemetry is { } value && (value.Front.Present || value.Rear.Present);
    }

    public static bool HasImuTelemetry(TelemetryData? telemetry)
    {
        return telemetry?.ImuData is { } imuData &&
               imuData.HasSamples &&
               imuData.ActiveLocations.Count > 0;
    }

    public static bool HasFramePitchRollTelemetry(TelemetryData? telemetry)
    {
        if (telemetry?.ImuData is not { } imuData ||
            !imuData.HasSamples ||
            !imuData.ActiveLocations.Contains((byte)ImuLocation.Frame))
        {
            return false;
        }

        var frameMeta = imuData.Meta.FirstOrDefault(meta => meta.LocationId == (byte)ImuLocation.Frame);
        return frameMeta is { AccelLsbPerG: > 0, GyroLsbPerDps: > 0 };
    }

    public static RecordedSignalAvailabilityState CreateSignalAvailability(
        TelemetryData? telemetry,
        IReadOnlyList<TrackPoint>? trackPoints)
    {
        var hasTravelTelemetry = HasTravelTelemetry(telemetry);
        return new RecordedSignalAvailabilityState(
            Travel: hasTravelTelemetry,
            Velocity: hasTravelTelemetry,
            Imu: HasImuTelemetry(telemetry),
            PitchRoll: HasFramePitchRollTelemetry(telemetry),
            Speed: TrackPointSeries.HasSpeedSeries(trackPoints),
            Elevation: TrackPointSeries.HasElevationSeries(trackPoints));
    }

    public static RecordedSignalPresentationState CreateSignalPresentation(
        TelemetryData? telemetry,
        IReadOnlyList<TrackPoint>? trackPoints)
    {
        var availability = CreateSignalAvailability(telemetry, trackPoints);
        var travelState = availability.Travel
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var imuState = availability.Imu
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var pitchRollState = availability.PitchRoll
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var speedState = availability.Speed
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var elevationState = availability.Elevation
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        return CreateSignalPresentation(
            travelState,
            travelState,
            imuState,
            pitchRollState,
            speedState,
            elevationState);
    }

    public static RecordedSignalPresentationState CreateLoadingSignalPresentation(bool mapExpected)
    {
        return CreateSignalPresentation(
            SurfacePresentationState.Loading("Loading travel signal data."),
            SurfacePresentationState.Loading("Loading velocity signal data."),
            SurfacePresentationState.Loading("Loading IMU signal data."),
            SurfacePresentationState.Loading("Loading pitch/roll signal data."),
            mapExpected ? SurfacePresentationState.Loading("Loading speed signal data.") : SurfacePresentationState.Hidden,
            mapExpected ? SurfacePresentationState.Loading("Loading elevation signal data.") : SurfacePresentationState.Hidden);
    }

    public static RecordedAnalysisPresentationState CreateAnalysisPresentation(
        TelemetryData? telemetry,
        TelemetryTimeRange? analysisRange,
        bool frontAnalysisAvailable,
        bool rearAnalysisAvailable,
        bool balanceAvailable)
    {
        if (telemetry is null)
        {
            return CreateHiddenAnalysisPresentationState();
        }

        var state = new RecordedAnalysisPresentationState(
            AnalysisSurfaceState.ForSuspension(telemetry, SuspensionType.Front, analysisRange),
            AnalysisSurfaceState.ForSuspension(telemetry, SuspensionType.Rear, analysisRange),
            AnalysisSurfaceState.ForBalance(telemetry, BalanceType.Compression, analysisRange),
            AnalysisSurfaceState.ForBalance(telemetry, BalanceType.Rebound, analysisRange),
            AnalysisSurfaceState.ForVibration(telemetry, SuspensionType.Front, ImuLocation.Fork, analysisRange),
            AnalysisSurfaceState.ForVibration(telemetry, SuspensionType.Front, ImuLocation.Frame, analysisRange),
            AnalysisSurfaceState.ForVibration(telemetry, SuspensionType.Rear, ImuLocation.Fork, analysisRange),
            AnalysisSurfaceState.ForVibration(telemetry, SuspensionType.Rear, ImuLocation.Frame, analysisRange));

        if (!frontAnalysisAvailable && state.FrontAnalysis.Kind != SurfaceStateKind.NoData)
        {
            state = state with
            {
                FrontAnalysis = SurfacePresentationState.Hidden,
                FrontForkVibration = SurfacePresentationState.Hidden,
                FrontFrameVibration = SurfacePresentationState.Hidden,
            };
        }

        if (!rearAnalysisAvailable && state.RearAnalysis.Kind != SurfaceStateKind.NoData)
        {
            state = state with
            {
                RearAnalysis = SurfacePresentationState.Hidden,
                RearForkVibration = SurfacePresentationState.Hidden,
                RearFrameVibration = SurfacePresentationState.Hidden,
            };
        }

        if (!balanceAvailable)
        {
            state = state with
            {
                CompressionBalance = SurfacePresentationState.Hidden,
                ReboundBalance = SurfacePresentationState.Hidden,
            };
        }

        return state;
    }

    public static SurfacePresentationState CreateMapState(
        IReadOnlyCollection<TrackPoint>? trackPoints,
        bool mapExpected)
    {
        if (trackPoints is { Count: > 0 })
        {
            return SurfacePresentationState.Ready;
        }

        return mapExpected
            ? SurfacePresentationState.WaitingForData("Waiting for map data.")
            : SurfacePresentationState.Hidden;
    }

    public static RecordedSignalPresentationState CreateHiddenSignalPresentationState()
    {
        return CreateSignalPresentation(
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);
    }

    public static RecordedSignalAvailabilityState CreateHiddenSignalAvailabilityState()
    {
        return new RecordedSignalAvailabilityState(
            Travel: false,
            Velocity: false,
            Imu: false,
            PitchRoll: false,
            Speed: false,
            Elevation: false);
    }

    public static RecordedAnalysisPresentationState CreateHiddenAnalysisPresentationState()
    {
        return new RecordedAnalysisPresentationState(
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);
    }

    public static RecordedAnalysisPresentationState CreateLoadingAnalysisPresentationState()
    {
        return new RecordedAnalysisPresentationState(
            SurfacePresentationState.Loading("Loading analysis."),
            SurfacePresentationState.Loading("Loading analysis."),
            SurfacePresentationState.Loading("Loading balance data."),
            SurfacePresentationState.Loading("Loading balance data."),
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);
    }

    private static RecordedSignalPresentationState CreateSignalPresentation(
        SurfacePresentationState travelState,
        SurfacePresentationState velocityState,
        SurfacePresentationState imuState,
        SurfacePresentationState pitchRollState,
        SurfacePresentationState speedState,
        SurfacePresentationState elevationState)
    {
        return new RecordedSignalPresentationState(
            Travel: travelState,
            Velocity: velocityState,
            Imu: imuState,
            PitchRoll: pitchRollState,
            Speed: speedState,
            Elevation: elevationState,
            ShowAirtime: false,
            ShowVelocityAirtime: false,
            ShowImuAirtime: false,
            ShowPitchRollAirtime: false,
            ShowSpeedAirtime: false,
            ShowElevationAirtime: false,
            ShowAnalysisSelection: false,
            ShowVelocityAnalysisSelection: false,
            ShowImuAnalysisSelection: false,
            ShowPitchRollAnalysisSelection: false,
            ShowSpeedAnalysisSelection: false,
            ShowElevationAnalysisSelection: false,
            TravelHeaderActions: [],
            VelocityHeaderActions: [],
            ImuHeaderActions: [],
            PitchRollHeaderActions: [],
            SpeedHeaderActions: [],
            ElevationHeaderActions: []);
    }
}
