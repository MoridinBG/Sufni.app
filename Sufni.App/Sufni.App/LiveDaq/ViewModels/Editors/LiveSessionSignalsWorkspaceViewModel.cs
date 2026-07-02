using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.MapsAndTracks.Models;
namespace Sufni.App.LiveDaq.ViewModels.Editors;

public sealed class LiveSessionSignalsWorkspaceViewModel : ObservableObject, ILiveSessionSignalsWorkspace
{
    private uint? sessionId;
    private bool travelExpected;
    private bool imuExpected;
    private bool pitchRollExpected;
    private bool gpsExpected;
    private bool hasTravelData;
    private bool hasImuData;
    private bool hasPitchRollData;
    private SignalDisplayPreferences signalDisplayPreferences = new();

    public IObservable<LiveSignalBatch> SignalBatches { get; }
    public LiveSessionPlotRanges PlotRanges { get; }
    public SessionTimelineLinkViewModel Timeline { get; }
    public IReadOnlyList<TrackPoint> TrackPoints
    {
        get => field;
        private set => SetProperty(ref field, value);
    } = [];

    public TrackTimeRange? TrackTimelineContext
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public SignalDisplayPreferences SignalDisplayPreferences
    {
        get => signalDisplayPreferences;
        private set => SetProperty(ref signalDisplayPreferences, value);
    }

    public SignalLayoutPreferences SignalLayoutPreferences
    {
        get => field;
        set => SetProperty(ref field, value);
    } = SignalLayoutPreferences.Default;

    public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();

    public SurfacePresentationState TravelSignalState
    {
        get => field;
        private set => SetProperty(ref field, value);
    } = SurfacePresentationState.Hidden;

    public SurfacePresentationState ImuSignalState
    {
        get => field;
        private set => SetProperty(ref field, value);
    } = SurfacePresentationState.Hidden;

    public SurfacePresentationState PitchRollSignalState
    {
        get => field;
        private set => SetProperty(ref field, value);
    } = SurfacePresentationState.Hidden;

    public SurfacePresentationState VelocitySignalState
    {
        get => field;
        private set => SetProperty(ref field, value);
    } = SurfacePresentationState.Hidden;

    public SurfacePresentationState SpeedSignalState
    {
        get => field;
        private set => SetProperty(ref field, value);
    } = SurfacePresentationState.Hidden;

    public SurfacePresentationState ElevationSignalState
    {
        get => field;
        private set => SetProperty(ref field, value);
    } = SurfacePresentationState.Hidden;

    public LiveSessionSignalsWorkspaceViewModel()
        : this(new SessionTimelineLinkViewModel(), LiveSessionPlotRanges.Default, Observable.Empty<LiveSignalBatch>())
    {
    }

    public LiveSessionSignalsWorkspaceViewModel(
        SessionTimelineLinkViewModel timeline,
        LiveSessionPlotRanges plotRanges,
        IObservable<LiveSignalBatch> signalBatches)
    {
        Timeline = timeline;
        PlotRanges = plotRanges;
        SignalBatches = signalBatches;
    }

    public void ApplySessionHeader(LiveSessionHeader? sessionHeader)
    {
        if (sessionHeader is null)
        {
            sessionId = null;
            travelExpected = false;
            imuExpected = false;
            pitchRollExpected = false;
            gpsExpected = false;
            hasTravelData = false;
            hasImuData = false;
            hasPitchRollData = false;
            TrackPoints = [];
            TrackTimelineContext = null;
            SourceVisibility.Clear();
            RefreshStates();
            return;
        }

        var sessionChanged = sessionId != sessionHeader.SessionId;
        sessionId = sessionHeader.SessionId;
        travelExpected = sessionHeader.AcceptedTravelHz > 0;
        var activeImuLocations = sessionHeader.GetActiveImuLocations();
        imuExpected = sessionHeader.AcceptedImuHz > 0 && activeImuLocations.Count > 0;
        pitchRollExpected = HasFramePitchRollSource(sessionHeader, activeImuLocations);
        gpsExpected = sessionHeader.AcceptedGpsFixHz > 0;

        if (sessionChanged)
        {
            hasTravelData = false;
            hasImuData = false;
            hasPitchRollData = false;
            TrackPoints = [];
            TrackTimelineContext = null;
            SourceVisibility.Clear();
        }

        RefreshStates();
    }

    public void ApplySignalBatch(LiveSignalBatch batch)
    {
        ApplySignalDataPresence(HasTravelData(batch), HasImuData(batch), HasPitchRollData(batch));
    }

    public void ApplySignalDisplayPreferences(SignalDisplayPreferences preferences)
    {
        SignalDisplayPreferences = preferences;
        RefreshStates();
    }

    public void ApplySignalDataPresence(bool hasTravelData, bool hasImuData, bool hasPitchRollData)
    {
        if (!this.hasTravelData && hasTravelData)
        {
            this.hasTravelData = true;
        }

        if (!this.hasImuData && hasImuData)
        {
            this.hasImuData = true;
        }

        if (!this.hasPitchRollData && hasPitchRollData)
        {
            this.hasPitchRollData = true;
        }

        RefreshStates();
    }

    public void ApplyTrackPresentation(IReadOnlyList<TrackPoint> points, TrackTimeRange? context)
    {
        TrackPoints = points;
        TrackTimelineContext = context;
        RefreshStates();
    }

    private static bool HasTravelData(LiveSignalBatch batch)
    {
        return batch.TravelTimes.Count > 0
            || batch.FrontTravel.Count > 0
            || batch.RearTravel.Count > 0
            || batch.VelocityTimes.Count > 0
            || batch.FrontVelocity.Count > 0
            || batch.RearVelocity.Count > 0;
    }

    private static bool HasImuData(LiveSignalBatch batch)
    {
        foreach (var series in batch.ImuTimes.Values)
        {
            if (series.Count > 0)
            {
                return true;
            }
        }

        foreach (var series in batch.ImuVibrationRms.Values)
        {
            if (series.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPitchRollData(LiveSignalBatch batch)
    {
        return batch.FramePitchRollTimes.Count > 0 ||
            batch.FramePitchDegrees.Count > 0 ||
            batch.FrameRollDegrees.Count > 0;
    }

    private static bool HasFramePitchRollSource(LiveSessionHeader sessionHeader, IReadOnlyList<LiveImuLocation> activeImuLocations)
    {
        return sessionHeader.AcceptedImuHz > 0 &&
            activeImuLocations.Contains(LiveImuLocation.Frame) &&
            sessionHeader.ImuCalibrationScales.GetAccelScale(LiveImuLocation.Frame) > 0 &&
            sessionHeader.ImuCalibrationScales.GetGyroScale(LiveImuLocation.Frame) > 0;
    }

    private void RefreshStates()
    {
        var travelState = !travelExpected
            ? SurfacePresentationState.Hidden
            : hasTravelData
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.WaitingForData("Waiting for live travel data.");

        var velocityState = !travelExpected
            ? SurfacePresentationState.Hidden
            : hasTravelData
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.WaitingForData("Waiting for live velocity data.");

        var imuState = !imuExpected
            ? SurfacePresentationState.Hidden
            : hasImuData
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.WaitingForData("Waiting for live IMU data.");

        var pitchRollState = !pitchRollExpected
            ? SurfacePresentationState.Hidden
            : hasPitchRollData
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.WaitingForData("Waiting for live pitch/roll data.");

        var hasSpeedSeries = TrackPointSeries.HasSpeedSeries(TrackPoints);
        var speedState = !gpsExpected
            ? SurfacePresentationState.Hidden
            : hasSpeedSeries
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.WaitingForData("Waiting for live speed data.");

        var hasElevationSeries = TrackPointSeries.HasElevationSeries(TrackPoints);
        var elevationState = !gpsExpected
            ? SurfacePresentationState.Hidden
            : hasElevationSeries
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.WaitingForData("Waiting for live elevation data.");

        TravelSignalState = travelState.ApplyPlotSelection(signalDisplayPreferences.Travel);
        VelocitySignalState = velocityState.ApplyPlotSelection(signalDisplayPreferences.Velocity);
        ImuSignalState = imuState.ApplyPlotSelection(signalDisplayPreferences.Imu);
        PitchRollSignalState = pitchRollState.ApplyPlotSelection(signalDisplayPreferences.PitchRoll);
        SpeedSignalState = speedState.ApplyPlotSelection(signalDisplayPreferences.Speed);
        ElevationSignalState = elevationState.ApplyPlotSelection(signalDisplayPreferences.Elevation);
    }
}
