using System.Collections.ObjectModel;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Pages.ViewModels.Editors;

internal sealed class RecordedPagePresentationApplier
{
    private readonly ObservableCollection<PageViewModelBase> pages;
    private readonly SpringPageViewModel springPage;
    private readonly DampingPageViewModel dampingPage;
    private readonly BalancePageViewModel balancePage;
    private readonly VibrationPageViewModel vibrationPage;
    private readonly SessionInsightsPageViewModel analysisPage;
    private readonly NotesPageViewModel notesPage;

    public RecordedPagePresentationApplier(
        ObservableCollection<PageViewModelBase> pages,
        SpringPageViewModel springPage,
        DampingPageViewModel dampingPage,
        BalancePageViewModel balancePage,
        VibrationPageViewModel vibrationPage,
        SessionInsightsPageViewModel analysisPage,
        NotesPageViewModel notesPage)
    {
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
        dampingPage.ApplyDampingPercentages(SessionDampingPercentages.Empty);
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
                var telemetry = loaded.Data.TelemetryPresentation.TelemetryData;
                EnsureBalancePage(
                    TelemetryStatistics.HasBalanceData(telemetry, BalanceType.Compression) ||
                    TelemetryStatistics.HasBalanceData(telemetry, BalanceType.Rebound));
                break;

            case SessionDetailLoadResult.IncompleteLocalData:
                ClearRecordedPresentation();
                break;

            case SessionDetailLoadResult.Failed:
                ClearRecordedPresentation();
                break;
        }
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
