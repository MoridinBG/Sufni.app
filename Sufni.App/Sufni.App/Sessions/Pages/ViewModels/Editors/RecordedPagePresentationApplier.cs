using System.Collections.ObjectModel;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Analysis.Services;

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

    public void ApplyRecordedLoadingStates()
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
            case SessionDetailLoadResult.IncompleteLocalData:
                ClearRecordedPresentation();
                break;

            case SessionDetailLoadResult.Failed:
                ClearRecordedPresentation();
                break;
        }
    }

    public void ApplyAnalysisAvailability(AnalysisAvailabilityResult availability) =>
        EnsureBalancePage(availability.FullSessionBalanceAvailable);

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
