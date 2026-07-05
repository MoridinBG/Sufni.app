using NSubstitute;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.Tests.ExtensionHost;

using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Extensibility.Views;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.Extensibility.Views;

public class RecordedSessionExtensionPagesControllerTests
{
    [Fact]
    public void PageContributions_InsertUsingExistingRequestedIndexBehavior()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages();
        _ = CreateController(manager, pages);

        manager.ExtensionSlots.Pages.Add(CreatePageContribution("extension-page", requestedIndex: 1));

        AssertPageOrder(
            pages,
            [
                "Signals",
                "Extension page",
                "Spring",
                "Strokes",
                "Damping",
                "Balance",
                "Vibration",
                "Insights",
            ]);
    }

    [Fact]
    public void AnalysisTabContributions_InsertBeforeMatchingBuiltInAnalysisPage()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages();
        _ = CreateController(manager, pages);

        manager.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution("analysis-tab", requestedIndex: 3));

        AssertPageOrder(
            pages,
            [
                "Signals",
                "Spring",
                "Strokes",
                "Damping",
                "Analysis tab",
                "Balance",
                "Vibration",
                "Insights",
            ]);
    }

    [Fact]
    public void AnalysisTabContributions_InsertBeforeNextBuiltInAnalysisPage_WhenBalanceIsAbsent()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages(includeBalance: false);
        _ = CreateController(manager, pages);

        manager.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution("analysis-tab", requestedIndex: 3));

        AssertPageOrder(
            pages,
            [
                "Signals",
                "Spring",
                "Strokes",
                "Damping",
                "Analysis tab",
                "Vibration",
                "Insights",
            ]);
    }

    [Fact]
    public void AnalysisTabContributions_DoNotCreateViewModelUntilPageViewModelIsResolved()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages();
        _ = CreateController(manager, pages);
        var createCount = 0;
        var viewModel = new TestContributionViewModel();
        manager.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution(
            "analysis-tab",
            requestedIndex: 3,
            createViewModel: () =>
            {
                createCount++;
                return viewModel;
            }));

        AssertPageOrder(
            pages,
            [
                "Signals",
                "Spring",
                "Strokes",
                "Damping",
                "Analysis tab",
                "Balance",
                "Vibration",
                "Insights",
            ]);
        Assert.Equal(0, createCount);

        var page = Assert.IsType<RecordedSessionExtensionPageViewModel>(
            pages.Single(page => page.DisplayName == "Analysis tab"));

        Assert.Same(viewModel, page.ViewModel);
        Assert.Same(viewModel, page.ViewModel);
        Assert.Equal(1, createCount);
    }

    [Fact]
    public void AnalysisTabRebuild_ReusesMaterializedViewModelForSameKey()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages();
        _ = CreateController(manager, pages);
        var createCount = 0;
        var viewModel = new TestContributionViewModel();
        var contribution = CreateAnalysisTabContribution(
            "analysis-tab",
            requestedIndex: 3,
            createViewModel: () =>
            {
                createCount++;
                return viewModel;
            });
        manager.ExtensionSlots.AnalysisTabs.Add(contribution);
        var page = Assert.IsType<RecordedSessionExtensionPageViewModel>(
            pages.Single(page => page.DisplayName == "Analysis tab"));
        _ = page.ViewModel;

        manager.ExtensionSlots.AnalysisTabs.ReplaceWith(
        [
            CreateAnalysisTabContribution(
                "analysis-tab",
                requestedIndex: 3,
                createViewModel: () =>
                {
                    createCount++;
                    return new TestContributionViewModel();
                }),
        ]);

        var rebuiltPage = Assert.IsType<RecordedSessionExtensionPageViewModel>(
            pages.Single(page => page.DisplayName == "Analysis tab"));

        Assert.Same(page, rebuiltPage);
        Assert.Same(viewModel, rebuiltPage.ViewModel);
        Assert.Equal(1, createCount);
    }

    [Fact]
    public void SlotReset_RemovesStaleAnalysisTabPages()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages();
        _ = CreateController(manager, pages);

        manager.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution("analysis-tab", requestedIndex: 3));
        manager.ExtensionSlots.AnalysisTabs.Clear();

        AssertPageOrder(
            pages,
            [
                "Signals",
                "Spring",
                "Strokes",
                "Damping",
                "Balance",
                "Vibration",
                "Insights",
            ]);
    }

    [Fact]
    public void SlotReset_DoesNotDisposeBorrowedStalePageContribution()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages();
        _ = CreateController(manager, pages);
        var viewModel = new DisposableContributionViewModel();

        manager.ExtensionSlots.Pages.Add(CreatePageContribution("extension-page", requestedIndex: 1, viewModel: viewModel));
        var page = Assert.IsType<RecordedSessionExtensionPageViewModel>(
            pages.Single(page => page.DisplayName == "Extension page"));
        Assert.Same(viewModel, page.ViewModel);

        manager.ExtensionSlots.Pages.Clear();

        Assert.False(viewModel.IsDisposed);
        Assert.Equal(0, viewModel.DisposeCount);
    }

    [Fact]
    public void SlotReset_DisposesMaterializedStaleAnalysisTabContribution()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages();
        _ = CreateController(manager, pages);
        var viewModel = new DisposableContributionViewModel();

        manager.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution(
            "analysis-tab",
            requestedIndex: 3,
            createViewModel: () => viewModel));
        var page = Assert.IsType<RecordedSessionExtensionPageViewModel>(
            pages.Single(page => page.DisplayName == "Analysis tab"));
        Assert.Same(viewModel, page.ViewModel);

        manager.ExtensionSlots.AnalysisTabs.Clear();

        Assert.True(viewModel.IsDisposed);
        Assert.Equal(1, viewModel.DisposeCount);
    }

    [Fact]
    public void AnalysisTabReorder_UpdatesPageOrderDeterministically()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages();
        _ = CreateController(manager, pages);
        var first = CreateAnalysisTabContribution("first", requestedIndex: 3, order: 2, displayName: "First");
        var second = CreateAnalysisTabContribution("second", requestedIndex: 3, order: 1, displayName: "Second");

        manager.ExtensionSlots.AnalysisTabs.Add(first);
        manager.ExtensionSlots.AnalysisTabs.Add(second);
        manager.ExtensionSlots.AnalysisTabs.ReplaceWith([first with { Order = 0 }, second]);

        AssertPageOrder(
            pages,
            [
                "Signals",
                "Spring",
                "Strokes",
                "Damping",
                "First",
                "Second",
                "Balance",
                "Vibration",
                "Insights",
            ]);
    }

    [Fact]
    public void RequestPageSelection_EmitsSelectedPageIndexIntent()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages();
        var actions = new RecordedSessionEditorActions();
        var intents = new List<RecordedSessionEditorIntent>();
        using var subscription = actions.Intents.Subscribe(intents.Add);
        var controller = CreateController(manager, pages, actions);

        manager.ExtensionSlots.Pages.Add(CreatePageContribution("extension-page", requestedIndex: 1));
        var contributedPage = Assert.Single(pages, page => page.DisplayName == "Extension page");

        controller.RequestRecordedSessionExtensionPageSelection("extension-page");

        var intent = Assert.IsType<RecordedSessionEditorIntent.SelectPageIndex>(Assert.Single(intents));
        Assert.Equal(pages.IndexOf(contributedPage), intent.PageIndex);
    }

    [Fact]
    public void Dispose_DisposesOwnedAnalysisTabViewModels_AndLeavesBorrowedPageViewModels()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages();
        var controller = CreateController(manager, pages);
        var pageViewModel = new DisposableContributionViewModel();
        var analysisTabViewModel = new DisposableContributionViewModel();
        manager.ExtensionSlots.Pages.Add(CreatePageContribution(
            "extension-page",
            requestedIndex: 1,
            viewModel: pageViewModel));
        manager.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution(
            "analysis-tab",
            requestedIndex: 3,
            createViewModel: () => analysisTabViewModel));

        var page = Assert.IsType<RecordedSessionExtensionPageViewModel>(
            pages.Single(page => page.DisplayName == "Extension page"));
        var analysisTab = Assert.IsType<RecordedSessionExtensionPageViewModel>(
            pages.Single(page => page.DisplayName == "Analysis tab"));
        Assert.Same(pageViewModel, page.ViewModel);
        Assert.Same(analysisTabViewModel, analysisTab.ViewModel);

        controller.Dispose();
        controller.Dispose();

        Assert.Equal(0, pageViewModel.DisposeCount);
        Assert.Equal(1, analysisTabViewModel.DisposeCount);
        Assert.DoesNotContain(page, pages);
        Assert.DoesNotContain(analysisTab, pages);
    }

    private static RecordedSessionExtensionPagesController CreateController(
        RecordedSessionExtensionManager manager,
        ObservableCollection<PageViewModelBase> pages,
        RecordedSessionEditorActions? actions = null)
    {
        return new RecordedSessionExtensionPagesController(
            manager,
            pages,
            actions ?? new RecordedSessionEditorActions());
    }

    private static RecordedSessionExtensionManager CreateManager()
    {
        var operationCoordinator = new RecordedSessionOperationCoordinator((_, _) => { }, () => { });
        return new RecordedSessionExtensionManager(
            Guid.NewGuid(),
            [],
            Substitute.For<IExtensionDatabaseConnection>(),
            Substitute.For<IRecordedSessionDataReader>(),
            new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(),
            operationCoordinator,
            new DelegatingRecordedSessionHostOperations(
                setAnalysisRange: null,
                clearAnalysisRange: null,
                setTimelineVisibleRange: null,
                addError: null,
                addNotification: null,
                startOperation: operationCoordinator.StartOperation,
                requestPageSelection: null));
    }

    private static ObservableCollection<PageViewModelBase> CreateBuiltInPages(bool includeBalance = true)
    {
        var pages = new ObservableCollection<PageViewModelBase>
        {
            new("Signals"),
            new("Spring"),
            new("Strokes"),
            new("Damping"),
        };

        if (includeBalance)
        {
            pages.Add(new PageViewModelBase("Balance"));
        }

        pages.Add(new PageViewModelBase("Vibration"));
        pages.Add(new PageViewModelBase("Insights"));
        return pages;
    }

    private static RecordedSessionPageContribution CreatePageContribution(
        string contributionId,
        int requestedIndex,
        IRecordedSessionPageContributionViewModel? viewModel = null)
    {
        return new RecordedSessionPageContribution(
            "extension",
            contributionId,
            Order: 0,
            "Extension page",
            viewModel ?? new TestContributionViewModel(),
            requestedIndex);
    }

    private static RecordedSessionAnalysisTabContribution CreateAnalysisTabContribution(
        string contributionId,
        int requestedIndex,
        int order = 0,
        string displayName = "Analysis tab",
        Func<IRecordedSessionAnalysisTabContributionViewModel>? createViewModel = null)
    {
        return new RecordedSessionAnalysisTabContribution(
            "extension",
            contributionId,
            order,
            displayName,
            requestedIndex,
            createViewModel ?? (() => new TestContributionViewModel()));
    }

    private static void AssertPageOrder(
        IReadOnlyList<PageViewModelBase> pages,
        IReadOnlyList<string> expectedDisplayNames)
    {
        Assert.Equal(expectedDisplayNames, pages.Select(page => page.DisplayName));
    }
}
