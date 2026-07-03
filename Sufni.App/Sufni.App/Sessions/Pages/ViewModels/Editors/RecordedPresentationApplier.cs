using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Presentation;
namespace Sufni.App.Sessions.Pages.ViewModels.Editors;

internal sealed class RecordedPresentationApplier
{
    private readonly SessionDetailViewModel owner;
    private readonly RecordedSessionContext context;
    private readonly ObservableCollection<PageViewModelBase> pages;
    private readonly SpringPageViewModel springPage;
    private readonly DampingPageViewModel dampingPage;
    private readonly BalancePageViewModel balancePage;
    private readonly VibrationPageViewModel vibrationPage;
    private readonly SessionInsightsPageViewModel analysisPage;
    private readonly NotesPageViewModel notesPage;
    private readonly PreferencesPageViewModel preferencesPage;
    private SurfacePresentationState recordedTravelSignalBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedVelocitySignalBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedImuSignalBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedPitchRollSignalBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedSpeedSignalBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedElevationSignalBaseState = SurfacePresentationState.Hidden;

    public RecordedPresentationApplier(
        SessionDetailViewModel owner,
        RecordedSessionContext context,
        ObservableCollection<PageViewModelBase> pages,
        SpringPageViewModel springPage,
        DampingPageViewModel dampingPage,
        BalancePageViewModel balancePage,
        VibrationPageViewModel vibrationPage,
        SessionInsightsPageViewModel analysisPage,
        NotesPageViewModel notesPage,
        PreferencesPageViewModel preferencesPage)
    {
        this.owner = owner;
        this.context = context;
        this.pages = pages;
        this.springPage = springPage;
        this.dampingPage = dampingPage;
        this.balancePage = balancePage;
        this.vibrationPage = vibrationPage;
        this.analysisPage = analysisPage;
        this.notesPage = notesPage;
        this.preferencesPage = preferencesPage;
    }

    public void ClearRecordedPresentation()
    {
        context.TelemetryData = null;
        context.FullTrackPoints = null;
        context.TrackPoints = null;
        context.MediaColumnWidth = null;
        owner.ApplyDampingPercentages(SessionDampingPercentages.Empty);
        HideVibrationStates();
        ApplyRecordedPlotAvailability(null);
        SetRecordedSignalBaseStates(
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);
    }

    public void ApplyRecordedLoadingStates(bool mapExpected)
    {
        context.ScreenState = SessionScreenPresentationState.Ready;
        ApplyRecordedPlotAvailability(null);
        SetRecordedSignalBaseStates(
            SurfacePresentationState.Loading("Loading travel signal data."),
            SurfacePresentationState.Loading("Loading velocity signal data."),
            SurfacePresentationState.Loading("Loading IMU signal data."),
            SurfacePresentationState.Loading("Loading pitch/roll signal data."),
            mapExpected ? SurfacePresentationState.Loading("Loading speed signal data.") : SurfacePresentationState.Hidden,
            mapExpected ? SurfacePresentationState.Loading("Loading elevation signal data.") : SurfacePresentationState.Hidden);
        context.FrontAnalysisState = SurfacePresentationState.Loading("Loading analysis.");
        context.RearAnalysisState = SurfacePresentationState.Loading("Loading analysis.");
        context.CompressionBalanceState = SurfacePresentationState.Loading("Loading balance data.");
        context.ReboundBalanceState = SurfacePresentationState.Loading("Loading balance data.");
        HideVibrationStates();
        context.MapState = mapExpected
            ? SurfacePresentationState.Loading("Loading map data.")
            : SurfacePresentationState.Hidden;
        springPage.FrontDistributionState = SurfacePresentationState.Loading("Loading spring chart.");
        springPage.RearDistributionState = SurfacePresentationState.Loading("Loading spring chart.");
        dampingPage.FrontDistributionState = SurfacePresentationState.Loading("Loading damping chart.");
        dampingPage.RearDistributionState = SurfacePresentationState.Loading("Loading damping chart.");
        balancePage.CompressionBalanceState = SurfacePresentationState.Loading("Loading balance chart.");
        balancePage.ReboundBalanceState = SurfacePresentationState.Loading("Loading balance chart.");
    }

    public void ApplyRecordedWaitingStates(bool mapExpected)
    {
        context.ScreenState = SessionScreenPresentationState.Ready;
        ApplyRecordedPlotAvailability(null);
        SetRecordedSignalBaseStates(
            SurfacePresentationState.WaitingForData("Waiting for travel data."),
            SurfacePresentationState.WaitingForData("Waiting for velocity data."),
            SurfacePresentationState.WaitingForData("Waiting for IMU data."),
            SurfacePresentationState.WaitingForData("Waiting for pitch/roll data."),
            mapExpected ? SurfacePresentationState.WaitingForData("Waiting for speed data.") : SurfacePresentationState.Hidden,
            mapExpected ? SurfacePresentationState.WaitingForData("Waiting for elevation data.") : SurfacePresentationState.Hidden);
        context.FrontAnalysisState = SurfacePresentationState.WaitingForData("Waiting for analysis data.");
        context.RearAnalysisState = SurfacePresentationState.WaitingForData("Waiting for analysis data.");
        context.CompressionBalanceState = SurfacePresentationState.WaitingForData("Waiting for balance data.");
        context.ReboundBalanceState = SurfacePresentationState.WaitingForData("Waiting for balance data.");
        HideVibrationStates();
        context.MapState = mapExpected
            ? SurfacePresentationState.WaitingForData("Waiting for map data.")
            : SurfacePresentationState.Hidden;
        springPage.FrontDistributionState = SurfacePresentationState.WaitingForData("Waiting for spring chart.");
        springPage.RearDistributionState = SurfacePresentationState.WaitingForData("Waiting for spring chart.");
        dampingPage.FrontDistributionState = SurfacePresentationState.WaitingForData("Waiting for damping chart.");
        dampingPage.RearDistributionState = SurfacePresentationState.WaitingForData("Waiting for damping chart.");
        balancePage.CompressionBalanceState = SurfacePresentationState.WaitingForData("Waiting for balance chart.");
        balancePage.ReboundBalanceState = SurfacePresentationState.WaitingForData("Waiting for balance chart.");
    }

    public void ApplyDesktopLoadResult(SessionDesktopLoadResult result)
    {
        switch (result)
        {
            case SessionDesktopLoadResult.Loaded loaded:
                owner.ApplyDampingSpeedCutoffContext(
                    loaded.Data.DampingSpeedCutoffs,
                    loaded.Data.DampingSpeedCutoffOwner);
                owner.ApplyTelemetryDataWithoutAnalysisRecompute(loaded.Data.TelemetryData);
                owner.SetSessionFullTrack(loaded.Data.FullTrackId);
                context.FullTrackPoints = loaded.Data.FullTrackPoints;
                context.TrackPoints = loaded.Data.TrackPoints;
                context.MediaColumnWidth = loaded.Data.MediaColumnWidth;
                owner.ApplyModeAwareDampingPercentages(loaded.Data.DampingPercentages);
                ApplyRecordedLoadedStates(loaded.Data);
                break;

            case SessionDesktopLoadResult.TelemetryPending:
                ClearRecordedPresentation();
                ApplyRecordedWaitingStates(owner.CurrentSessionFullTrack is not null);
                break;

            case SessionDesktopLoadResult.Failed failed:
                ClearRecordedPresentation();
                context.ScreenState = SessionScreenPresentationState.Error($"Could not load session data: {failed.ErrorMessage}");
                break;
        }
    }

    public void ApplyMobileLoadResult(SessionMobileLoadResult result)
    {
        switch (result)
        {
            case SessionMobileLoadResult.LoadedFromCache loadedFromCache:
                ApplyCachePresentation(loadedFromCache.Data);
                owner.ApplyTelemetryDataWithoutAnalysisRecompute(loadedFromCache.Telemetry);
                ApplyMobileExtendedAnalysisStates(
                    loadedFromCache.Telemetry,
                    HasFrontCacheAnalysis(loadedFromCache.Data),
                    HasRearCacheAnalysis(loadedFromCache.Data),
                    loadedFromCache.Data.BalanceAvailable);
                ApplyRecordedReadySignalStates(context.TelemetryData);
                ApplyMobileTrackPresentation(loadedFromCache.TrackData);
                context.ScreenState = SessionScreenPresentationState.Ready;
                owner.IsComplete = true;
                break;

            case SessionMobileLoadResult.BuiltCache builtCache:
                ApplyCachePresentation(builtCache.Data);
                owner.ApplyTelemetryDataWithoutAnalysisRecompute(builtCache.Telemetry);
                ApplyMobileExtendedAnalysisStates(
                    builtCache.Telemetry,
                    HasFrontCacheAnalysis(builtCache.Data),
                    HasRearCacheAnalysis(builtCache.Data),
                    builtCache.Data.BalanceAvailable);
                ApplyRecordedReadySignalStates(context.TelemetryData);
                ApplyMobileTrackPresentation(builtCache.TrackData);
                context.ScreenState = SessionScreenPresentationState.Ready;
                owner.IsComplete = true;
                break;

            case SessionMobileLoadResult.TelemetryPending:
                ApplyRecordedWaitingStates(mapExpected: false);
                break;

            case SessionMobileLoadResult.Failed failed:
                context.ScreenState = SessionScreenPresentationState.Error($"Could not load session data: {failed.ErrorMessage}");
                break;
        }
    }

    public void RefreshAnalysisRangeStates()
    {
        if (context.TelemetryData is { } telemetry)
        {
            ApplyAnalysisRangeStates(telemetry);
        }
    }

    public void ApplyRecordedTrackSignalStates()
    {
        ApplyRecordedPlotAvailability(context.TelemetryData);
        recordedSpeedSignalBaseState = TrackPointSeries.HasSpeedSeries(context.TrackPoints)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        recordedElevationSignalBaseState = TrackPointSeries.HasElevationSeries(context.TrackPoints)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        RefreshRecordedSignalStates();
    }

    public void ApplyRecordedTrackPresentationData(SessionTrackPresentationData trackData)
    {
        owner.SetSessionFullTrack(trackData.FullTrackId);
        context.FullTrackPoints = trackData.FullTrackPoints;
        context.TrackPoints = trackData.TrackPoints;
        context.MediaColumnWidth = trackData.MediaColumnWidth;
        context.MapState = CreateMapState(trackData.TrackPoints, trackData.FullTrackId is not null);
    }

    public void RefreshRecordedSignalStates()
    {
        // Every available signal is shown; hiding is handled by collapsing rows
        // in the signals view, so row state follows availability alone.
        context.TravelSignalState = recordedTravelSignalBaseState;
        context.VelocitySignalState = recordedVelocitySignalBaseState;
        context.ImuSignalState = recordedImuSignalBaseState;
        context.PitchRollSignalState = recordedPitchRollSignalBaseState;
        context.SpeedSignalState = recordedSpeedSignalBaseState;
        context.ElevationSignalState = recordedElevationSignalBaseState;
    }

    private void ApplyCachePresentation(SessionCachePresentationData data)
    {
        owner.ApplyDampingSpeedCutoffContext(data.DampingSpeedCutoffs, data.DampingSpeedCutoffOwner);

        var hasFrontTravelDistribution = !string.IsNullOrWhiteSpace(data.FrontTravelDistribution);
        var hasRearTravelDistribution = !string.IsNullOrWhiteSpace(data.RearTravelDistribution);
        var hasFrontVelocityDistribution = !string.IsNullOrWhiteSpace(data.FrontVelocityDistribution);
        var hasRearVelocityDistribution = !string.IsNullOrWhiteSpace(data.RearVelocityDistribution);
        var hasCompressionBalance = !string.IsNullOrWhiteSpace(data.CompressionBalance);
        var hasReboundBalance = !string.IsNullOrWhiteSpace(data.ReboundBalance);

        springPage.FrontTravelDistribution = data.FrontTravelDistribution;
        springPage.RearTravelDistribution = data.RearTravelDistribution;
        springPage.FrontDistributionState = hasFrontTravelDistribution
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        springPage.RearDistributionState = hasRearTravelDistribution
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        dampingPage.FrontVelocityDistribution = data.FrontVelocityDistribution;
        dampingPage.RearVelocityDistribution = data.RearVelocityDistribution;
        dampingPage.FrontDistributionState = hasFrontVelocityDistribution
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        dampingPage.RearDistributionState = hasRearVelocityDistribution
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        context.FrontAnalysisState = springPage.FrontDistributionState.ReservesLayout || dampingPage.FrontDistributionState.ReservesLayout
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        context.RearAnalysisState = springPage.RearDistributionState.ReservesLayout || dampingPage.RearDistributionState.ReservesLayout
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        owner.ApplyDampingPercentages(data.DampingPercentages);
        balancePage.CompressionBalance = data.CompressionBalance;
        balancePage.ReboundBalance = data.ReboundBalance;
        balancePage.CompressionBalanceState = hasCompressionBalance
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        balancePage.ReboundBalanceState = hasReboundBalance
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        context.CompressionBalanceState = balancePage.CompressionBalanceState;
        context.ReboundBalanceState = balancePage.ReboundBalanceState;
        HideVibrationStates();
        EnsureBalancePage(data.BalanceAvailable);
    }

    private static bool HasTravelTelemetry(TelemetryData? telemetry)
    {
        return telemetry is { } value && (value.Front.Present || value.Rear.Present);
    }

    private static bool HasImuTelemetry(TelemetryData? telemetry)
    {
        return telemetry?.ImuData is { } imuData &&
               imuData.HasSamples &&
               imuData.ActiveLocations.Count > 0;
    }

    private static bool HasFramePitchRollTelemetry(TelemetryData? telemetry)
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

    private void ApplyAnalysisRangeStates(TelemetryData telemetry)
    {
        context.FrontAnalysisState = AnalysisSurfaceState.ForSuspension(telemetry, SuspensionType.Front, context.AnalysisRange);
        context.RearAnalysisState = AnalysisSurfaceState.ForSuspension(telemetry, SuspensionType.Rear, context.AnalysisRange);
        context.CompressionBalanceState = AnalysisSurfaceState.ForBalance(telemetry, BalanceType.Compression, context.AnalysisRange);
        context.ReboundBalanceState = AnalysisSurfaceState.ForBalance(telemetry, BalanceType.Rebound, context.AnalysisRange);
        context.FrontForkVibrationState = AnalysisSurfaceState.ForVibration(telemetry, SuspensionType.Front, ImuLocation.Fork, context.AnalysisRange);
        context.FrontFrameVibrationState = AnalysisSurfaceState.ForVibration(telemetry, SuspensionType.Front, ImuLocation.Frame, context.AnalysisRange);
        context.RearForkVibrationState = AnalysisSurfaceState.ForVibration(telemetry, SuspensionType.Rear, ImuLocation.Fork, context.AnalysisRange);
        context.RearFrameVibrationState = AnalysisSurfaceState.ForVibration(telemetry, SuspensionType.Rear, ImuLocation.Frame, context.AnalysisRange);
    }

    private void HideVibrationStates()
    {
        context.FrontForkVibrationState = SurfacePresentationState.Hidden;
        context.FrontFrameVibrationState = SurfacePresentationState.Hidden;
        context.RearForkVibrationState = SurfacePresentationState.Hidden;
        context.RearFrameVibrationState = SurfacePresentationState.Hidden;
    }

    private static SurfacePresentationState CreateMapState(IReadOnlyCollection<TrackPoint>? trackPoints, bool mapExpected)
    {
        if (trackPoints is { Count: > 0 })
        {
            return SurfacePresentationState.Ready;
        }

        return mapExpected
            ? SurfacePresentationState.WaitingForData("Waiting for map data.")
            : SurfacePresentationState.Hidden;
    }

    private void ApplyRecordedLoadedStates(SessionTelemetryPresentationData data)
    {
        context.ScreenState = SessionScreenPresentationState.Ready;
        ApplyRecordedReadySignalStates(data.TelemetryData);

        if (data.TelemetryData is { } telemetry)
        {
            ApplyAnalysisRangeStates(telemetry);
        }
        else
        {
            context.FrontAnalysisState = SurfacePresentationState.Hidden;
            context.RearAnalysisState = SurfacePresentationState.Hidden;
            context.CompressionBalanceState = SurfacePresentationState.Hidden;
            context.ReboundBalanceState = SurfacePresentationState.Hidden;
            HideVibrationStates();
        }

        context.MapState = CreateMapState(data.TrackPoints, data.FullTrackId is not null);
    }

    private static bool HasFrontCacheAnalysis(SessionCachePresentationData data)
    {
        return !string.IsNullOrWhiteSpace(data.FrontTravelDistribution)
               || !string.IsNullOrWhiteSpace(data.FrontVelocityDistribution);
    }

    private static bool HasRearCacheAnalysis(SessionCachePresentationData data)
    {
        return !string.IsNullOrWhiteSpace(data.RearTravelDistribution)
               || !string.IsNullOrWhiteSpace(data.RearVelocityDistribution);
    }

    private void ApplyMobileExtendedAnalysisStates(
        TelemetryData? telemetry,
        bool frontAnalysisAvailable,
        bool rearAnalysisAvailable,
        bool balanceAvailable)
    {
        if (telemetry is null)
        {
            context.FrontAnalysisState = SurfacePresentationState.Hidden;
            context.RearAnalysisState = SurfacePresentationState.Hidden;
            context.CompressionBalanceState = SurfacePresentationState.Hidden;
            context.ReboundBalanceState = SurfacePresentationState.Hidden;
            HideVibrationStates();
            return;
        }

        ApplyAnalysisRangeStates(telemetry);
        if (!frontAnalysisAvailable && context.FrontAnalysisState.Kind != SurfaceStateKind.NoData)
        {
            context.FrontAnalysisState = SurfacePresentationState.Hidden;
            context.FrontForkVibrationState = SurfacePresentationState.Hidden;
            context.FrontFrameVibrationState = SurfacePresentationState.Hidden;
        }

        if (!rearAnalysisAvailable && context.RearAnalysisState.Kind != SurfaceStateKind.NoData)
        {
            context.RearAnalysisState = SurfacePresentationState.Hidden;
            context.RearForkVibrationState = SurfacePresentationState.Hidden;
            context.RearFrameVibrationState = SurfacePresentationState.Hidden;
        }

        if (!balanceAvailable)
        {
            context.CompressionBalanceState = SurfacePresentationState.Hidden;
            context.ReboundBalanceState = SurfacePresentationState.Hidden;
        }
    }

    private void ApplyMobileTrackPresentation(SessionTrackPresentationData? trackData)
    {
        owner.SetSessionFullTrack(trackData?.FullTrackId);
        context.FullTrackPoints = trackData?.FullTrackPoints;
        context.TrackPoints = trackData?.TrackPoints;
        context.MediaColumnWidth = trackData?.MediaColumnWidth;
        context.MapState = CreateMapState(trackData?.TrackPoints, trackData?.FullTrackId is not null);
    }

    private void ApplyRecordedPlotAvailability(TelemetryData? telemetry)
    {
        var hasTravelTelemetry = HasTravelTelemetry(telemetry);
        var hasImuTelemetry = HasImuTelemetry(telemetry);
        var hasFramePitchRollTelemetry = HasFramePitchRollTelemetry(telemetry);
        var hasSpeedSeries = TrackPointSeries.HasSpeedSeries(context.TrackPoints);
        var hasElevationSeries = TrackPointSeries.HasElevationSeries(context.TrackPoints);
        preferencesPage.ApplySignalAvailability(
            hasTravelTelemetry,
            hasTravelTelemetry,
            hasImuTelemetry,
            hasFramePitchRollTelemetry,
            hasSpeedSeries,
            hasElevationSeries);
    }

    private void ApplyRecordedReadySignalStates(TelemetryData? telemetry)
    {
        var hasTravelTelemetry = HasTravelTelemetry(telemetry);
        var hasImuTelemetry = HasImuTelemetry(telemetry);
        var hasFramePitchRollTelemetry = HasFramePitchRollTelemetry(telemetry);
        var hasSpeedSeries = TrackPointSeries.HasSpeedSeries(context.TrackPoints);
        var hasElevationSeries = TrackPointSeries.HasElevationSeries(context.TrackPoints);

        preferencesPage.ApplySignalAvailability(
            hasTravelTelemetry,
            hasTravelTelemetry,
            hasImuTelemetry,
            hasFramePitchRollTelemetry,
            hasSpeedSeries,
            hasElevationSeries);

        var travelState = hasTravelTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var imuState = hasImuTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var pitchRollState = hasFramePitchRollTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var speedState = hasSpeedSeries
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        var elevationState = hasElevationSeries
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        SetRecordedSignalBaseStates(travelState, travelState, imuState, pitchRollState, speedState, elevationState);
    }

    private void SetRecordedSignalBaseStates(
        SurfacePresentationState travelState,
        SurfacePresentationState velocityState,
        SurfacePresentationState imuState,
        SurfacePresentationState pitchRollState,
        SurfacePresentationState speedState,
        SurfacePresentationState elevationState)
    {
        recordedTravelSignalBaseState = travelState;
        recordedVelocitySignalBaseState = velocityState;
        recordedImuSignalBaseState = imuState;
        recordedPitchRollSignalBaseState = pitchRollState;
        recordedSpeedSignalBaseState = speedState;
        recordedElevationSignalBaseState = elevationState;
        RefreshRecordedSignalStates();
    }

    private void EnsureBalancePage(bool balanceAvailable)
    {
        var containsBalancePage = pages.Contains(balancePage);
        if (balanceAvailable)
        {
            if (containsBalancePage)
            {
                return;
            }

            var insertIndex = pages.IndexOf(vibrationPage);
            if (insertIndex < 0)
            {
                insertIndex = pages.IndexOf(analysisPage);
            }

            if (insertIndex < 0)
            {
                insertIndex = pages.IndexOf(notesPage);
            }

            if (insertIndex < 0)
            {
                pages.Add(balancePage);
            }
            else
            {
                pages.Insert(insertIndex, balancePage);
            }

            return;
        }

        if (containsBalancePage)
        {
            pages.Remove(balancePage);
        }
    }
}
