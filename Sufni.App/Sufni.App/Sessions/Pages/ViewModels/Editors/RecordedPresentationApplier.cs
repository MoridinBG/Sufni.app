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
        owner.SetFullTrackPoints(null);
        owner.SetTrackPoints(null);
        context.MediaColumnWidth = null;
        owner.ApplyDampingPercentages(SessionDampingPercentages.Empty);
        context.FrontAnalysisState = SurfacePresentationState.Hidden;
        context.RearAnalysisState = SurfacePresentationState.Hidden;
        context.CompressionBalanceState = SurfacePresentationState.Hidden;
        context.ReboundBalanceState = SurfacePresentationState.Hidden;
        HideVibrationStates();
        ApplyRecordedPlotAvailability(null);
        SetRecordedSignalStates(
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);
        springPage.FrontDistributionState = SurfacePresentationState.Hidden;
        springPage.RearDistributionState = SurfacePresentationState.Hidden;
        dampingPage.FrontDistributionState = SurfacePresentationState.Hidden;
        dampingPage.RearDistributionState = SurfacePresentationState.Hidden;
        balancePage.CompressionBalanceState = SurfacePresentationState.Hidden;
        balancePage.ReboundBalanceState = SurfacePresentationState.Hidden;
    }

    public void ApplyRecordedLoadingStates(bool mapExpected)
    {
        context.ScreenState = SessionScreenPresentationState.Ready;
        ApplyRecordedPlotAvailability(null);
        SetRecordedSignalStates(
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

    public void ApplyLoadResult(SessionDetailLoadResult result)
    {
        switch (result)
        {
            case SessionDetailLoadResult.Loaded loaded:
                var telemetryPresentation = loaded.Data.TelemetryPresentation;
                var cachePresentation = loaded.Data.CachePresentation;
                ApplyCachePresentation(cachePresentation);
                owner.ApplyTelemetryDataWithoutAnalysisRecompute(telemetryPresentation.TelemetryData);
                owner.SetSessionFullTrack(telemetryPresentation.FullTrackId);
                owner.SetFullTrackPoints(telemetryPresentation.FullTrackPoints);
                owner.SetTrackPoints(telemetryPresentation.TrackPoints);
                context.MediaColumnWidth = telemetryPresentation.MediaColumnWidth;
                owner.ApplyModeAwareDampingPercentages(telemetryPresentation.DampingPercentages);
                ApplyMobileExtendedAnalysisStates(
                    telemetryPresentation.TelemetryData,
                    HasFrontCacheAnalysis(cachePresentation),
                    HasRearCacheAnalysis(cachePresentation),
                    cachePresentation.BalanceAvailable);
                ApplyRecordedReadySignalStates(telemetryPresentation.TelemetryData);
                context.MapState = CreateMapState(
                    telemetryPresentation.TrackPoints,
                    telemetryPresentation.FullTrackId is not null);
                context.ScreenState = SessionScreenPresentationState.Ready;
                owner.IsComplete = true;
                break;

            case SessionDetailLoadResult.IncompleteLocalData incomplete:
                ClearRecordedPresentation();
                context.ScreenState = SessionScreenPresentationState.IncompleteLocalData(
                    FormatIncompleteLocalDataMessage(incomplete.Missing));
                owner.IsComplete = context.SessionSnapshot?.HasProcessedData ?? false;
                break;

            case SessionDetailLoadResult.Failed failed:
                ClearRecordedPresentation();
                context.ScreenState = SessionScreenPresentationState.Error($"Could not load session data: {failed.ErrorMessage}");
                break;
        }
    }

    private static string FormatIncompleteLocalDataMessage(MissingSessionData missing)
    {
        var missingParts = new List<string>();
        if (missing.ProcessedTelemetryBlob)
        {
            missingParts.Add("processed telemetry");
        }

        if (missing.RecordedSourceMissingOrHashMismatch)
        {
            missingParts.Add("recorded source");
        }

        return missingParts.Count == 0
            ? "Local session data is incomplete. Run sync and try again."
            : $"Local session data is incomplete: {string.Join(", ", missingParts)}. Run sync and try again.";
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
        context.SpeedSignalState = TrackPointSeries.HasSpeedSeries(context.TrackPoints)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        context.ElevationSignalState = TrackPointSeries.HasElevationSeries(context.TrackPoints)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
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

        SetRecordedSignalStates(travelState, travelState, imuState, pitchRollState, speedState, elevationState);
    }

    private void SetRecordedSignalStates(
        SurfacePresentationState travelState,
        SurfacePresentationState velocityState,
        SurfacePresentationState imuState,
        SurfacePresentationState pitchRollState,
        SurfacePresentationState speedState,
        SurfacePresentationState elevationState)
    {
        context.TravelSignalState = travelState;
        context.VelocitySignalState = velocityState;
        context.ImuSignalState = imuState;
        context.PitchRollSignalState = pitchRollState;
        context.SpeedSignalState = speedState;
        context.ElevationSignalState = elevationState;
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
