using System.Collections.ObjectModel;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.SessionDetails;

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
        owner.ApplyDampingPercentages(SessionDampingPercentages.Empty);
        springPage.FrontDistributionState = SurfacePresentationState.Hidden;
        springPage.RearDistributionState = SurfacePresentationState.Hidden;
        dampingPage.FrontDistributionState = SurfacePresentationState.Hidden;
        dampingPage.RearDistributionState = SurfacePresentationState.Hidden;
        balancePage.CompressionBalanceState = SurfacePresentationState.Hidden;
        balancePage.ReboundBalanceState = SurfacePresentationState.Hidden;
    }

    public void ApplyRecordedLoadingStates(bool mapExpected)
    {
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
                owner.ApplyModeAwareDampingPercentages(telemetryPresentation.DampingPercentages);
                owner.IsComplete = true;
                break;

            case SessionDetailLoadResult.IncompleteLocalData:
                ClearRecordedPresentation();
                owner.IsComplete = owner.CurrentSessionSnapshot?.HasProcessedData ?? false;
                break;

            case SessionDetailLoadResult.Failed:
                ClearRecordedPresentation();
                break;
        }
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
        EnsureBalancePage(data.BalanceAvailable);
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
