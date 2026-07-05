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

internal sealed class RecordedPagePresentationApplier
{
    private readonly SessionDetailViewModel owner;
    private readonly ObservableCollection<PageViewModelBase> pages;
    private readonly SpringPageViewModel springPage;
    private readonly DampingPageViewModel dampingPage;
    private readonly BalancePageViewModel balancePage;
    private readonly VibrationPageViewModel vibrationPage;
    private readonly SessionInsightsPageViewModel analysisPage;
    private readonly NotesPageViewModel notesPage;

    public RecordedPagePresentationApplier(
        SessionDetailViewModel owner,
        ObservableCollection<PageViewModelBase> pages,
        SpringPageViewModel springPage,
        DampingPageViewModel dampingPage,
        BalancePageViewModel balancePage,
        VibrationPageViewModel vibrationPage,
        SessionInsightsPageViewModel analysisPage,
        NotesPageViewModel notesPage)
    {
        this.owner = owner;
        this.pages = pages;
        this.springPage = springPage;
        this.dampingPage = dampingPage;
        this.balancePage = balancePage;
        this.vibrationPage = vibrationPage;
        this.analysisPage = analysisPage;
        this.notesPage = notesPage;
    }

    public void ClearRecordedPresentation()
    {
        owner.SetTelemetryData(null);
        owner.SetFullTrackPoints(null);
        owner.SetTrackPoints(null);
        owner.SetMediaColumnWidth(null);
        owner.ApplyDampingPercentages(SessionDampingPercentages.Empty);
        owner.SetRecordedAnalysisStates(RecordedSessionPresentationDeriver.CreateHiddenAnalysisPresentationState());
        ApplyRecordedSignalPresentation(
            RecordedSessionPresentationDeriver.CreateHiddenSignalPresentationState());
        springPage.FrontDistributionState = SurfacePresentationState.Hidden;
        springPage.RearDistributionState = SurfacePresentationState.Hidden;
        dampingPage.FrontDistributionState = SurfacePresentationState.Hidden;
        dampingPage.RearDistributionState = SurfacePresentationState.Hidden;
        balancePage.CompressionBalanceState = SurfacePresentationState.Hidden;
        balancePage.ReboundBalanceState = SurfacePresentationState.Hidden;
    }

    public void ApplyRecordedLoadingStates(bool mapExpected)
    {
        owner.SetScreenState(SessionScreenPresentationState.Ready);
        ApplyRecordedSignalPresentation(
            RecordedSessionPresentationDeriver.CreateLoadingSignalPresentation(mapExpected));
        owner.SetRecordedAnalysisStates(RecordedSessionPresentationDeriver.CreateLoadingAnalysisPresentationState());
        owner.SetMapState(mapExpected
            ? SurfacePresentationState.Loading("Loading map data.")
            : SurfacePresentationState.Hidden);
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
                owner.SetMediaColumnWidth(telemetryPresentation.MediaColumnWidth);
                owner.ApplyModeAwareDampingPercentages(telemetryPresentation.DampingPercentages);
                ApplyMobileExtendedAnalysisStates(
                    telemetryPresentation.TelemetryData,
                    HasFrontCacheAnalysis(cachePresentation),
                    HasRearCacheAnalysis(cachePresentation),
                    cachePresentation.BalanceAvailable);
                ApplyRecordedReadySignalStates(telemetryPresentation.TelemetryData);
                owner.SetMapState(RecordedSessionPresentationDeriver.CreateMapState(
                    telemetryPresentation.TrackPoints,
                    telemetryPresentation.FullTrackId is not null));
                owner.SetScreenState(SessionScreenPresentationState.Ready);
                owner.IsComplete = true;
                break;

            case SessionDetailLoadResult.IncompleteLocalData incomplete:
                ClearRecordedPresentation();
                owner.SetScreenState(SessionScreenPresentationState.IncompleteLocalData(
                    FormatIncompleteLocalDataMessage(incomplete.Missing)));
                owner.IsComplete = owner.CurrentSessionSnapshot?.HasProcessedData ?? false;
                break;

            case SessionDetailLoadResult.Failed failed:
                ClearRecordedPresentation();
                owner.SetScreenState(SessionScreenPresentationState.Error($"Could not load session data: {failed.ErrorMessage}"));
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
        if (owner.CurrentTelemetryData is { } telemetry)
        {
            ApplyAnalysisRangeStates(telemetry);
        }
    }

    public void ApplyRecordedTrackSignalStates()
    {
        owner.SetTrackDerivedSignalStates(
            TrackPointSeries.HasSpeedSeries(owner.CurrentTrackPoints)
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.Hidden,
            TrackPointSeries.HasElevationSeries(owner.CurrentTrackPoints)
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.Hidden);
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

        owner.ApplyDampingPercentages(data.DampingPercentages);
        balancePage.CompressionBalance = data.CompressionBalance;
        balancePage.ReboundBalance = data.ReboundBalance;
        balancePage.CompressionBalanceState = hasCompressionBalance
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        balancePage.ReboundBalanceState = hasReboundBalance
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        owner.SetRecordedAnalysisStates(new RecordedAnalysisPresentationState(
            springPage.FrontDistributionState.ReservesLayout || dampingPage.FrontDistributionState.ReservesLayout
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.Hidden,
            springPage.RearDistributionState.ReservesLayout || dampingPage.RearDistributionState.ReservesLayout
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.Hidden,
            balancePage.CompressionBalanceState,
            balancePage.ReboundBalanceState,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden));
        EnsureBalancePage(data.BalanceAvailable);
    }

    private void ApplyAnalysisRangeStates(TelemetryData telemetry)
    {
        owner.SetRecordedAnalysisStates(RecordedSessionPresentationDeriver.CreateAnalysisPresentation(
            telemetry,
            owner.CurrentAnalysisRange,
            frontAnalysisAvailable: true,
            rearAnalysisAvailable: true,
            balanceAvailable: true));
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
            owner.SetRecordedAnalysisStates(RecordedSessionPresentationDeriver.CreateHiddenAnalysisPresentationState());
            return;
        }

        owner.SetRecordedAnalysisStates(RecordedSessionPresentationDeriver.CreateAnalysisPresentation(
            telemetry,
            owner.CurrentAnalysisRange,
            frontAnalysisAvailable,
            rearAnalysisAvailable,
            balanceAvailable));
    }

    private void ApplyRecordedReadySignalStates(TelemetryData? telemetry)
    {
        ApplyRecordedSignalPresentation(RecordedSessionPresentationDeriver.CreateSignalPresentation(
            telemetry,
            owner.CurrentTrackPoints));
    }

    private void ApplyRecordedSignalPresentation(RecordedSignalPresentationState state)
    {
        owner.SetRecordedSignalStates(
            state.Travel,
            state.Velocity,
            state.Imu,
            state.PitchRoll,
            state.Speed,
            state.Elevation);
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
