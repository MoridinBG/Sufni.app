using Sufni.App.Sessions.Presentation;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Plots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Shared.Common;

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

    public void ApplySignalDisplayPreferences(SignalDisplayPreferences preferences)
    {
        SignalDisplayPreferences = preferences;
        RefreshStates();
    }

    public void ApplySignalPresence(LiveSignalPresence presence)
    {
        hasTravelData = presence.HasTravelData;
        hasImuData = presence.HasImuData;
        hasPitchRollData = presence.HasPitchRollData;
        RefreshStates();
    }

    public void ApplyTrackPresentation(IReadOnlyList<TrackPoint> points, TrackTimeRange? context)
    {
        TrackPoints = points;
        TrackTimelineContext = context;
        RefreshStates();
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
