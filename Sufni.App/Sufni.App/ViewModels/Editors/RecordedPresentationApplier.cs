using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.SessionDetails;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

internal sealed class RecordedPresentationApplier
{
    private readonly SessionDetailViewModel owner;
    private readonly RecordedSessionContext context;
    private readonly ObservableCollection<PageViewModelBase> pages;
    private readonly SpringPageViewModel springPage;
    private readonly DamperPageViewModel damperPage;
    private readonly BalancePageViewModel balancePage;
    private readonly VibrationPageViewModel vibrationPage;
    private readonly SessionAnalysisPageViewModel analysisPage;
    private readonly NotesPageViewModel notesPage;
    private readonly PreferencesPageViewModel preferencesPage;
    private SurfacePresentationState recordedTravelGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedVelocityGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedImuGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedPitchRollGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedSpeedGraphBaseState = SurfacePresentationState.Hidden;
    private SurfacePresentationState recordedElevationGraphBaseState = SurfacePresentationState.Hidden;

    public RecordedPresentationApplier(
        SessionDetailViewModel owner,
        RecordedSessionContext context,
        ObservableCollection<PageViewModelBase> pages,
        SpringPageViewModel springPage,
        DamperPageViewModel damperPage,
        BalancePageViewModel balancePage,
        VibrationPageViewModel vibrationPage,
        SessionAnalysisPageViewModel analysisPage,
        NotesPageViewModel notesPage,
        PreferencesPageViewModel preferencesPage)
    {
        this.owner = owner;
        this.context = context;
        this.pages = pages;
        this.springPage = springPage;
        this.damperPage = damperPage;
        this.balancePage = balancePage;
        this.vibrationPage = vibrationPage;
        this.analysisPage = analysisPage;
        this.notesPage = notesPage;
        this.preferencesPage = preferencesPage;
    }

    public void ClearRecordedPresentation()
    {
        owner.TelemetryData = null;
        owner.FullTrackPoints = null;
        owner.TrackPoints = null;
        owner.MediaColumnWidth = null;
        owner.ApplyDamperPercentages(SessionDamperPercentages.Empty);
        HideVibrationStates();
        ApplyRecordedPlotAvailability(null);
        SetRecordedGraphBaseStates(
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);
    }

    public void ApplyRecordedLoadingStates(bool mapExpected)
    {
        owner.ScreenState = SessionScreenPresentationState.Ready;
        ApplyRecordedPlotAvailability(null);
        SetRecordedGraphBaseStates(
            SurfacePresentationState.Loading("Loading travel graphs."),
            SurfacePresentationState.Loading("Loading velocity graph."),
            SurfacePresentationState.Loading("Loading IMU graph."),
            SurfacePresentationState.Loading("Loading pitch/roll graph."),
            mapExpected ? SurfacePresentationState.Loading("Loading speed graph.") : SurfacePresentationState.Hidden,
            mapExpected ? SurfacePresentationState.Loading("Loading elevation graph.") : SurfacePresentationState.Hidden);
        owner.FrontStatisticsState = SurfacePresentationState.Loading("Loading statistics.");
        owner.RearStatisticsState = SurfacePresentationState.Loading("Loading statistics.");
        owner.CompressionBalanceState = SurfacePresentationState.Loading("Loading balance data.");
        owner.ReboundBalanceState = SurfacePresentationState.Loading("Loading balance data.");
        HideVibrationStates();
        owner.MapState = mapExpected
            ? SurfacePresentationState.Loading("Loading map data.")
            : SurfacePresentationState.Hidden;
        springPage.FrontHistogramState = SurfacePresentationState.Loading("Loading spring chart.");
        springPage.RearHistogramState = SurfacePresentationState.Loading("Loading spring chart.");
        damperPage.FrontHistogramState = SurfacePresentationState.Loading("Loading damping chart.");
        damperPage.RearHistogramState = SurfacePresentationState.Loading("Loading damping chart.");
        balancePage.CompressionBalanceState = SurfacePresentationState.Loading("Loading balance chart.");
        balancePage.ReboundBalanceState = SurfacePresentationState.Loading("Loading balance chart.");
    }

    public void ApplyRecordedWaitingStates(bool mapExpected)
    {
        owner.ScreenState = SessionScreenPresentationState.Ready;
        ApplyRecordedPlotAvailability(null);
        SetRecordedGraphBaseStates(
            SurfacePresentationState.WaitingForData("Waiting for travel data."),
            SurfacePresentationState.WaitingForData("Waiting for velocity data."),
            SurfacePresentationState.WaitingForData("Waiting for IMU data."),
            SurfacePresentationState.WaitingForData("Waiting for pitch/roll data."),
            mapExpected ? SurfacePresentationState.WaitingForData("Waiting for speed data.") : SurfacePresentationState.Hidden,
            mapExpected ? SurfacePresentationState.WaitingForData("Waiting for elevation data.") : SurfacePresentationState.Hidden);
        owner.FrontStatisticsState = SurfacePresentationState.WaitingForData("Waiting for statistics.");
        owner.RearStatisticsState = SurfacePresentationState.WaitingForData("Waiting for statistics.");
        owner.CompressionBalanceState = SurfacePresentationState.WaitingForData("Waiting for balance data.");
        owner.ReboundBalanceState = SurfacePresentationState.WaitingForData("Waiting for balance data.");
        HideVibrationStates();
        owner.MapState = mapExpected
            ? SurfacePresentationState.WaitingForData("Waiting for map data.")
            : SurfacePresentationState.Hidden;
        springPage.FrontHistogramState = SurfacePresentationState.WaitingForData("Waiting for spring chart.");
        springPage.RearHistogramState = SurfacePresentationState.WaitingForData("Waiting for spring chart.");
        damperPage.FrontHistogramState = SurfacePresentationState.WaitingForData("Waiting for damping chart.");
        damperPage.RearHistogramState = SurfacePresentationState.WaitingForData("Waiting for damping chart.");
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
                owner.FullTrackPoints = loaded.Data.FullTrackPoints;
                owner.TrackPoints = loaded.Data.TrackPoints;
                owner.MediaColumnWidth = loaded.Data.MediaColumnWidth;
                owner.ApplyModeAwareDamperPercentages(loaded.Data.DamperPercentages);
                ApplyRecordedLoadedStates(loaded.Data);
                owner.RecomputeSessionAnalysis();
                break;

            case SessionDesktopLoadResult.TelemetryPending:
                ClearRecordedPresentation();
                ApplyRecordedWaitingStates(owner.CurrentSessionFullTrack is not null);
                break;

            case SessionDesktopLoadResult.Failed failed:
                ClearRecordedPresentation();
                owner.ScreenState = SessionScreenPresentationState.Error($"Could not load session data: {failed.ErrorMessage}");
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
                ApplyMobileExtendedStatisticsStates(
                    loadedFromCache.Telemetry,
                    HasFrontCacheStatistics(loadedFromCache.Data),
                    HasRearCacheStatistics(loadedFromCache.Data),
                    loadedFromCache.Data.BalanceAvailable);
                ApplyRecordedReadyGraphStates(owner.TelemetryData);
                ApplyMobileTrackPresentation(loadedFromCache.TrackData);
                owner.ScreenState = SessionScreenPresentationState.Ready;
                owner.IsComplete = true;
                owner.RecomputeSessionAnalysis();
                break;

            case SessionMobileLoadResult.BuiltCache builtCache:
                ApplyCachePresentation(builtCache.Data);
                owner.ApplyTelemetryDataWithoutAnalysisRecompute(builtCache.Telemetry);
                ApplyMobileExtendedStatisticsStates(
                    builtCache.Telemetry,
                    HasFrontCacheStatistics(builtCache.Data),
                    HasRearCacheStatistics(builtCache.Data),
                    builtCache.Data.BalanceAvailable);
                ApplyRecordedReadyGraphStates(owner.TelemetryData);
                ApplyMobileTrackPresentation(builtCache.TrackData);
                owner.ScreenState = SessionScreenPresentationState.Ready;
                owner.IsComplete = true;
                owner.RecomputeSessionAnalysis();
                break;

            case SessionMobileLoadResult.TelemetryPending:
                ApplyRecordedWaitingStates(mapExpected: false);
                break;

            case SessionMobileLoadResult.Failed failed:
                owner.ScreenState = SessionScreenPresentationState.Error($"Could not load session data: {failed.ErrorMessage}");
                break;
        }
    }

    public void RefreshAnalysisRangeStates()
    {
        if (owner.TelemetryData is { } telemetry)
        {
            ApplyAnalysisRangeStates(telemetry);
        }
    }

    public void ApplyRecordedTrackGraphStates()
    {
        ApplyRecordedPlotAvailability(owner.TelemetryData);
        recordedSpeedGraphBaseState = TrackPointSeries.HasSpeedSeries(owner.TrackPoints)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        recordedElevationGraphBaseState = TrackPointSeries.HasElevationSeries(owner.TrackPoints)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        RefreshRecordedGraphStates(owner.RecordedPlotPreferences);
    }

    public void RefreshRecordedGraphStates(SessionPlotPreferences preferences)
    {
        context.TravelGraphState = recordedTravelGraphBaseState.ApplyPlotSelection(preferences.Travel);
        context.VelocityGraphState = recordedVelocityGraphBaseState.ApplyPlotSelection(preferences.Velocity);
        context.ImuGraphState = recordedImuGraphBaseState.ApplyPlotSelection(preferences.Imu);
        context.PitchRollGraphState = recordedPitchRollGraphBaseState.ApplyPlotSelection(preferences.PitchRoll);
        context.SpeedGraphState = recordedSpeedGraphBaseState.ApplyPlotSelection(preferences.Speed);
        context.ElevationGraphState = recordedElevationGraphBaseState.ApplyPlotSelection(preferences.Elevation);
    }

    private void ApplyCachePresentation(SessionCachePresentationData data)
    {
        owner.ApplyDampingSpeedCutoffContext(data.DampingSpeedCutoffs, data.DampingSpeedCutoffOwner);

        var hasFrontTravelHistogram = !string.IsNullOrWhiteSpace(data.FrontTravelHistogram);
        var hasRearTravelHistogram = !string.IsNullOrWhiteSpace(data.RearTravelHistogram);
        var hasFrontVelocityHistogram = !string.IsNullOrWhiteSpace(data.FrontVelocityHistogram);
        var hasRearVelocityHistogram = !string.IsNullOrWhiteSpace(data.RearVelocityHistogram);
        var hasCompressionBalance = !string.IsNullOrWhiteSpace(data.CompressionBalance);
        var hasReboundBalance = !string.IsNullOrWhiteSpace(data.ReboundBalance);

        springPage.FrontTravelHistogram = data.FrontTravelHistogram;
        springPage.RearTravelHistogram = data.RearTravelHistogram;
        springPage.FrontHistogramState = hasFrontTravelHistogram
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        springPage.RearHistogramState = hasRearTravelHistogram
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        damperPage.FrontVelocityHistogram = data.FrontVelocityHistogram;
        damperPage.RearVelocityHistogram = data.RearVelocityHistogram;
        damperPage.FrontHistogramState = hasFrontVelocityHistogram
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        damperPage.RearHistogramState = hasRearVelocityHistogram
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        owner.FrontStatisticsState = springPage.FrontHistogramState.ReservesLayout || damperPage.FrontHistogramState.ReservesLayout
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        owner.RearStatisticsState = springPage.RearHistogramState.ReservesLayout || damperPage.RearHistogramState.ReservesLayout
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;

        owner.ApplyDamperPercentages(data.DamperPercentages);
        balancePage.CompressionBalance = data.CompressionBalance;
        balancePage.ReboundBalance = data.ReboundBalance;
        balancePage.CompressionBalanceState = hasCompressionBalance
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        balancePage.ReboundBalanceState = hasReboundBalance
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        owner.CompressionBalanceState = balancePage.CompressionBalanceState;
        owner.ReboundBalanceState = balancePage.ReboundBalanceState;
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
               imuData.Records.Count > 0 &&
               imuData.ActiveLocations.Count > 0;
    }

    private static bool HasFramePitchRollTelemetry(TelemetryData? telemetry)
    {
        if (telemetry?.ImuData is not { } imuData ||
            imuData.Records.Count == 0 ||
            !imuData.ActiveLocations.Contains((byte)ImuLocation.Frame))
        {
            return false;
        }

        var frameMeta = imuData.Meta.FirstOrDefault(meta => meta.LocationId == (byte)ImuLocation.Frame);
        return frameMeta is { AccelLsbPerG: > 0, GyroLsbPerDps: > 0 };
    }

    private void ApplyAnalysisRangeStates(TelemetryData telemetry)
    {
        owner.FrontStatisticsState = SessionStatisticsSurfaceState.ForSuspension(telemetry, SuspensionType.Front, owner.AnalysisRange);
        owner.RearStatisticsState = SessionStatisticsSurfaceState.ForSuspension(telemetry, SuspensionType.Rear, owner.AnalysisRange);
        owner.CompressionBalanceState = SessionStatisticsSurfaceState.ForBalance(telemetry, BalanceType.Compression, owner.AnalysisRange);
        owner.ReboundBalanceState = SessionStatisticsSurfaceState.ForBalance(telemetry, BalanceType.Rebound, owner.AnalysisRange);
        owner.FrontForkVibrationState = SessionStatisticsSurfaceState.ForVibration(telemetry, SuspensionType.Front, ImuLocation.Fork, owner.AnalysisRange);
        owner.FrontFrameVibrationState = SessionStatisticsSurfaceState.ForVibration(telemetry, SuspensionType.Front, ImuLocation.Frame, owner.AnalysisRange);
        owner.RearForkVibrationState = SessionStatisticsSurfaceState.ForVibration(telemetry, SuspensionType.Rear, ImuLocation.Fork, owner.AnalysisRange);
        owner.RearFrameVibrationState = SessionStatisticsSurfaceState.ForVibration(telemetry, SuspensionType.Rear, ImuLocation.Frame, owner.AnalysisRange);
    }

    private void HideVibrationStates()
    {
        owner.FrontForkVibrationState = SurfacePresentationState.Hidden;
        owner.FrontFrameVibrationState = SurfacePresentationState.Hidden;
        owner.RearForkVibrationState = SurfacePresentationState.Hidden;
        owner.RearFrameVibrationState = SurfacePresentationState.Hidden;
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
        owner.ScreenState = SessionScreenPresentationState.Ready;
        ApplyRecordedReadyGraphStates(data.TelemetryData);

        if (data.TelemetryData is { } telemetry)
        {
            ApplyAnalysisRangeStates(telemetry);
        }
        else
        {
            owner.FrontStatisticsState = SurfacePresentationState.Hidden;
            owner.RearStatisticsState = SurfacePresentationState.Hidden;
            owner.CompressionBalanceState = SurfacePresentationState.Hidden;
            owner.ReboundBalanceState = SurfacePresentationState.Hidden;
            HideVibrationStates();
        }

        owner.MapState = CreateMapState(data.TrackPoints, data.FullTrackId is not null);
    }

    private static bool HasFrontCacheStatistics(SessionCachePresentationData data)
    {
        return !string.IsNullOrWhiteSpace(data.FrontTravelHistogram)
               || !string.IsNullOrWhiteSpace(data.FrontVelocityHistogram);
    }

    private static bool HasRearCacheStatistics(SessionCachePresentationData data)
    {
        return !string.IsNullOrWhiteSpace(data.RearTravelHistogram)
               || !string.IsNullOrWhiteSpace(data.RearVelocityHistogram);
    }

    private void ApplyMobileExtendedStatisticsStates(
        TelemetryData? telemetry,
        bool frontStatisticsAvailable,
        bool rearStatisticsAvailable,
        bool balanceAvailable)
    {
        if (telemetry is null)
        {
            owner.FrontStatisticsState = SurfacePresentationState.Hidden;
            owner.RearStatisticsState = SurfacePresentationState.Hidden;
            owner.CompressionBalanceState = SurfacePresentationState.Hidden;
            owner.ReboundBalanceState = SurfacePresentationState.Hidden;
            HideVibrationStates();
            return;
        }

        ApplyAnalysisRangeStates(telemetry);
        if (!frontStatisticsAvailable && owner.FrontStatisticsState.Kind != SurfaceStateKind.NoData)
        {
            owner.FrontStatisticsState = SurfacePresentationState.Hidden;
            owner.FrontForkVibrationState = SurfacePresentationState.Hidden;
            owner.FrontFrameVibrationState = SurfacePresentationState.Hidden;
        }

        if (!rearStatisticsAvailable && owner.RearStatisticsState.Kind != SurfaceStateKind.NoData)
        {
            owner.RearStatisticsState = SurfacePresentationState.Hidden;
            owner.RearForkVibrationState = SurfacePresentationState.Hidden;
            owner.RearFrameVibrationState = SurfacePresentationState.Hidden;
        }

        if (!balanceAvailable)
        {
            owner.CompressionBalanceState = SurfacePresentationState.Hidden;
            owner.ReboundBalanceState = SurfacePresentationState.Hidden;
        }
    }

    private void ApplyMobileTrackPresentation(SessionTrackPresentationData? trackData)
    {
        owner.SetSessionFullTrack(trackData?.FullTrackId);
        owner.FullTrackPoints = trackData?.FullTrackPoints;
        owner.TrackPoints = trackData?.TrackPoints;
        owner.MediaColumnWidth = trackData?.MediaColumnWidth;
        owner.MapState = CreateMapState(trackData?.TrackPoints, trackData?.FullTrackId is not null);
    }

    private void ApplyRecordedPlotAvailability(TelemetryData? telemetry)
    {
        var hasTravelTelemetry = HasTravelTelemetry(telemetry);
        var hasImuTelemetry = HasImuTelemetry(telemetry);
        var hasFramePitchRollTelemetry = HasFramePitchRollTelemetry(telemetry);
        var hasSpeedSeries = TrackPointSeries.HasSpeedSeries(owner.TrackPoints);
        var hasElevationSeries = TrackPointSeries.HasElevationSeries(owner.TrackPoints);
        preferencesPage.ApplyPlotAvailability(
            hasTravelTelemetry,
            hasTravelTelemetry,
            hasImuTelemetry,
            hasFramePitchRollTelemetry,
            hasSpeedSeries,
            hasElevationSeries);
    }

    private void ApplyRecordedReadyGraphStates(TelemetryData? telemetry)
    {
        var hasTravelTelemetry = HasTravelTelemetry(telemetry);
        var hasImuTelemetry = HasImuTelemetry(telemetry);
        var hasFramePitchRollTelemetry = HasFramePitchRollTelemetry(telemetry);
        var hasSpeedSeries = TrackPointSeries.HasSpeedSeries(owner.TrackPoints);
        var hasElevationSeries = TrackPointSeries.HasElevationSeries(owner.TrackPoints);

        preferencesPage.ApplyPlotAvailability(
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

        SetRecordedGraphBaseStates(travelState, travelState, imuState, pitchRollState, speedState, elevationState);
    }

    private void SetRecordedGraphBaseStates(
        SurfacePresentationState travelState,
        SurfacePresentationState velocityState,
        SurfacePresentationState imuState,
        SurfacePresentationState pitchRollState,
        SurfacePresentationState speedState,
        SurfacePresentationState elevationState)
    {
        recordedTravelGraphBaseState = travelState;
        recordedVelocityGraphBaseState = velocityState;
        recordedImuGraphBaseState = imuState;
        recordedPitchRollGraphBaseState = pitchRollState;
        recordedSpeedGraphBaseState = speedState;
        recordedElevationGraphBaseState = elevationState;
        RefreshRecordedGraphStates(owner.RecordedPlotPreferences);
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
