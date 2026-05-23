using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.ViewModels;

namespace Sufni.App.ViewModels.Editors;

public sealed class LiveSessionGraphWorkspaceViewModel : ViewModelBase, ILiveSessionGraphWorkspace
{
    private SurfacePresentationState travelGraphState = SurfacePresentationState.Hidden;
    private SurfacePresentationState velocityGraphState = SurfacePresentationState.Hidden;
    private SurfacePresentationState imuGraphState = SurfacePresentationState.Hidden;
    private SurfacePresentationState pitchRollGraphState = SurfacePresentationState.Hidden;
    private SurfacePresentationState speedGraphState = SurfacePresentationState.Hidden;
    private SurfacePresentationState elevationGraphState = SurfacePresentationState.Hidden;
    private uint? sessionId;
    private bool travelExpected;
    private bool imuExpected;
    private bool pitchRollExpected;
    private bool gpsExpected;
    private bool hasTravelData;
    private bool hasImuData;
    private bool hasPitchRollData;
    private IReadOnlyList<TrackPoint> trackPoints = [];
    private TrackTimeRange? trackTimelineContext;
    private SessionPlotPreferences plotPreferences = new();
    private SessionGraphPreferences graphPreferences = SessionGraphPreferences.Default;

    public IObservable<LiveGraphBatch> GraphBatches { get; }
    public LiveSessionPlotRanges PlotRanges { get; }
    public SessionTimelineLinkViewModel Timeline { get; }
    public IReadOnlyList<TrackPoint> TrackPoints
    {
        get => trackPoints;
        private set => SetProperty(ref trackPoints, value);
    }

    public TrackTimeRange? TrackTimelineContext
    {
        get => trackTimelineContext;
        private set => SetProperty(ref trackTimelineContext, value);
    }

    public SessionPlotPreferences PlotPreferences
    {
        get => plotPreferences;
        private set => SetProperty(ref plotPreferences, value);
    }

    public SessionGraphPreferences GraphPreferences
    {
        get => graphPreferences;
        set => SetProperty(ref graphPreferences, value);
    }

    public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();

    public SurfacePresentationState TravelGraphState
    {
        get => travelGraphState;
        private set => SetProperty(ref travelGraphState, value);
    }

    public SurfacePresentationState ImuGraphState
    {
        get => imuGraphState;
        private set => SetProperty(ref imuGraphState, value);
    }

    public SurfacePresentationState PitchRollGraphState
    {
        get => pitchRollGraphState;
        private set => SetProperty(ref pitchRollGraphState, value);
    }

    public SurfacePresentationState VelocityGraphState
    {
        get => velocityGraphState;
        private set => SetProperty(ref velocityGraphState, value);
    }

    public SurfacePresentationState SpeedGraphState
    {
        get => speedGraphState;
        private set => SetProperty(ref speedGraphState, value);
    }

    public SurfacePresentationState ElevationGraphState
    {
        get => elevationGraphState;
        private set => SetProperty(ref elevationGraphState, value);
    }

    public LiveSessionGraphWorkspaceViewModel()
        : this(new SessionTimelineLinkViewModel(), LiveSessionPlotRanges.Default, Observable.Empty<LiveGraphBatch>())
    {
    }

    public LiveSessionGraphWorkspaceViewModel(
        SessionTimelineLinkViewModel timeline,
        LiveSessionPlotRanges plotRanges,
        IObservable<LiveGraphBatch> graphBatches)
    {
        Timeline = timeline;
        PlotRanges = plotRanges;
        GraphBatches = graphBatches;
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
        }

        RefreshStates();
    }

    public void ApplyGraphBatch(LiveGraphBatch batch)
    {
        ApplyGraphDataPresence(HasTravelData(batch), HasImuData(batch), HasPitchRollData(batch));
    }

    public void ApplyPlotPreferences(SessionPlotPreferences preferences)
    {
        PlotPreferences = preferences;
        RefreshStates();
    }

    public void ApplyGraphDataPresence(bool hasTravelData, bool hasImuData, bool hasPitchRollData)
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

    private static bool HasTravelData(LiveGraphBatch batch)
    {
        return batch.TravelTimes.Count > 0
            || batch.FrontTravel.Count > 0
            || batch.RearTravel.Count > 0
            || batch.VelocityTimes.Count > 0
            || batch.FrontVelocity.Count > 0
            || batch.RearVelocity.Count > 0;
    }

    private static bool HasImuData(LiveGraphBatch batch)
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

    private static bool HasPitchRollData(LiveGraphBatch batch)
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

        TravelGraphState = travelState.ApplyPlotSelection(plotPreferences.Travel);
        VelocityGraphState = velocityState.ApplyPlotSelection(plotPreferences.Velocity);
        ImuGraphState = imuState.ApplyPlotSelection(plotPreferences.Imu);
        PitchRollGraphState = pitchRollState.ApplyPlotSelection(plotPreferences.PitchRoll);
        SpeedGraphState = speedState.ApplyPlotSelection(plotPreferences.Speed);
        ElevationGraphState = elevationState.ApplyPlotSelection(plotPreferences.Elevation);
    }
}
