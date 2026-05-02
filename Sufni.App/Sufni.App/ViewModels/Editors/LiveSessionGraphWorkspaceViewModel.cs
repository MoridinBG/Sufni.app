using System;
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
    private uint? sessionId;
    private bool travelExpected;
    private bool imuExpected;
    private bool hasTravelData;
    private bool hasImuData;
    private SessionPlotPreferences plotPreferences = new();

    public IObservable<LiveGraphBatch> GraphBatches { get; }
    public LiveSessionPlotRanges PlotRanges { get; }
    public SessionTimelineLinkViewModel Timeline { get; }
    public SessionPlotPreferences PlotPreferences
    {
        get => plotPreferences;
        private set => SetProperty(ref plotPreferences, value);
    }
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

    public SurfacePresentationState VelocityGraphState
    {
        get => velocityGraphState;
        private set => SetProperty(ref velocityGraphState, value);
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
            hasTravelData = false;
            hasImuData = false;
            RefreshStates();
            return;
        }

        var sessionChanged = sessionId != sessionHeader.SessionId;
        sessionId = sessionHeader.SessionId;
        travelExpected = sessionHeader.AcceptedTravelHz > 0;
        imuExpected = sessionHeader.AcceptedImuHz > 0 && sessionHeader.GetActiveImuLocations().Count > 0;

        if (sessionChanged)
        {
            hasTravelData = false;
            hasImuData = false;
        }

        RefreshStates();
    }

    public void ApplyGraphBatch(LiveGraphBatch batch)
    {
        ApplyGraphDataPresence(HasTravelData(batch), HasImuData(batch));
    }

    public void ApplyPlotPreferences(SessionPlotPreferences preferences)
    {
        PlotPreferences = preferences;
        RefreshStates();
    }

    public void ApplyGraphDataPresence(bool hasTravelData, bool hasImuData)
    {
        if (!this.hasTravelData && hasTravelData)
        {
            this.hasTravelData = true;
        }

        if (!this.hasImuData && hasImuData)
        {
            this.hasImuData = true;
        }

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

        foreach (var series in batch.ImuMagnitudes.Values)
        {
            if (series.Count > 0)
            {
                return true;
            }
        }

        return false;
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

        TravelGraphState = travelState.ApplyPlotSelection(plotPreferences.Travel);
        VelocityGraphState = velocityState.ApplyPlotSelection(plotPreferences.Velocity);
        ImuGraphState = imuState.ApplyPlotSelection(plotPreferences.Imu);
    }
}