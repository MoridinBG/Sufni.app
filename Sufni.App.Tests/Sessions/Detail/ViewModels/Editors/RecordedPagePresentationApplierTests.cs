using System.Collections.ObjectModel;
using NSubstitute;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Shared.Common;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class RecordedPagePresentationApplierTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Availability_InsertsBalanceBeforeNextAnalysisPage_AndRemovesWhenUnavailable(int missingFollowers)
    {
        var workspace = Substitute.For<ISessionAnalysisWorkspace>();
        var spring = new SpringPageViewModel();
        var damping = new DampingPageViewModel();
        var balance = new BalancePageViewModel();
        var vibration = new VibrationPageViewModel(workspace);
        var insights = new SessionInsightsPageViewModel(workspace);
        var notes = new NotesPageViewModel();
        var followers = new PageViewModelBase[] { vibration, insights, notes }.Skip(missingFollowers).ToArray();
        var pages = new ObservableCollection<PageViewModelBase>(new PageViewModelBase[] { spring, damping }.Concat(followers));
        var sut = new RecordedPagePresentationApplier(pages, spring, damping, balance, vibration, insights, notes);
        var emptyRange = new AnalysisAvailabilityResult(AnalysisDataAvailability.Empty, AnalysisDataAvailability.Empty,
            AnalysisDataAvailability.Empty, AnalysisDataAvailability.Empty, AnalysisDataAvailability.Absent,
            AnalysisDataAvailability.Absent, AnalysisDataAvailability.Absent, AnalysisDataAvailability.Absent, true);

        sut.ApplyAnalysisAvailability(emptyRange);
        sut.ApplyAnalysisAvailability(emptyRange);

        Assert.Equal(new PageViewModelBase[] { spring, damping, balance }.Concat(followers), pages);
        sut.ApplyAnalysisAvailability(emptyRange with { FullSessionBalanceAvailable = false });
        Assert.Equal(new PageViewModelBase[] { spring, damping }.Concat(followers), pages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableLoad_ClearsPendingChartsAndPreviousPercentages(bool incomplete)
    {
        var workspace = Substitute.For<ISessionAnalysisWorkspace>();
        var spring = new SpringPageViewModel();
        var damping = new DampingPageViewModel();
        var balance = new BalancePageViewModel();
        var sut = new RecordedPagePresentationApplier([], spring, damping, balance,
            new VibrationPageViewModel(workspace), new SessionInsightsPageViewModel(workspace), new NotesPageViewModel());
        sut.ApplyRecordedLoadingStates();
        damping.ApplyDampingPercentages(new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8));

        sut.ApplyLoadResult(incomplete
            ? new SessionDetailLoadResult.IncompleteLocalData(Guid.NewGuid(), new(true, false))
            : new SessionDetailLoadResult.Failed("diagnostic"));

        Assert.Equal(SurfaceStateKind.Hidden, spring.FrontDistributionState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, damping.RearDistributionState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, balance.CompressionBalanceState.Kind);
        Assert.Null(damping.FrontHscPercentage);
        Assert.Null(damping.RearLsrPercentage);
    }
}
