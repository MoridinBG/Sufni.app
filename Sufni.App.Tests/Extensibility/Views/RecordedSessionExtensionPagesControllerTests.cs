using NSubstitute;
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
        var context = CreateBuiltInContext();
        _ = CreateController(manager, context);

        manager.ExtensionSlots.Pages.Add(CreatePageContribution("extension-page", requestedIndex: 1));

        AssertPageOrder(
            context.Pages,
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
        var context = CreateBuiltInContext();
        _ = CreateController(manager, context);

        manager.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution("analysis-tab", requestedIndex: 3));

        AssertPageOrder(
            context.Pages,
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
        var context = CreateBuiltInContext(includeBalance: false);
        _ = CreateController(manager, context);

        manager.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution("analysis-tab", requestedIndex: 3));

        AssertPageOrder(
            context.Pages,
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
        var context = CreateBuiltInContext();
        _ = CreateController(manager, context);
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
            context.Pages,
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
            context.Pages.Single(page => page.DisplayName == "Analysis tab"));

        Assert.Same(viewModel, page.ViewModel);
        Assert.Same(viewModel, page.ViewModel);
        Assert.Equal(1, createCount);
    }

    [Fact]
    public void AnalysisTabRebuild_ReusesMaterializedViewModelForSameKey()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext();
        _ = CreateController(manager, context);
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
            context.Pages.Single(page => page.DisplayName == "Analysis tab"));
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
            context.Pages.Single(page => page.DisplayName == "Analysis tab"));

        Assert.Same(page, rebuiltPage);
        Assert.Same(viewModel, rebuiltPage.ViewModel);
        Assert.Equal(1, createCount);
    }

    [Fact]
    public void SlotReset_RemovesStaleAnalysisTabPages()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext();
        _ = CreateController(manager, context);

        manager.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution("analysis-tab", requestedIndex: 3));
        manager.ExtensionSlots.AnalysisTabs.Clear();

        AssertPageOrder(
            context.Pages,
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
        var context = CreateBuiltInContext();
        _ = CreateController(manager, context);
        var viewModel = new DisposableContributionViewModel();

        manager.ExtensionSlots.Pages.Add(CreatePageContribution("extension-page", requestedIndex: 1, viewModel: viewModel));
        var page = Assert.IsType<RecordedSessionExtensionPageViewModel>(
            context.Pages.Single(page => page.DisplayName == "Extension page"));
        Assert.Same(viewModel, page.ViewModel);

        manager.ExtensionSlots.Pages.Clear();

        Assert.False(viewModel.IsDisposed);
        Assert.Equal(0, viewModel.DisposeCount);
    }

    [Fact]
    public void SlotReset_DisposesMaterializedStaleAnalysisTabContribution()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext();
        _ = CreateController(manager, context);
        var viewModel = new DisposableContributionViewModel();

        manager.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution(
            "analysis-tab",
            requestedIndex: 3,
            createViewModel: () => viewModel));
        var page = Assert.IsType<RecordedSessionExtensionPageViewModel>(
            context.Pages.Single(page => page.DisplayName == "Analysis tab"));
        Assert.Same(viewModel, page.ViewModel);

        manager.ExtensionSlots.AnalysisTabs.Clear();

        Assert.True(viewModel.IsDisposed);
        Assert.Equal(1, viewModel.DisposeCount);
    }

    [Fact]
    public void AnalysisTabReorder_UpdatesPageOrderDeterministically()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext();
        _ = CreateController(manager, context);
        var first = CreateAnalysisTabContribution("first", requestedIndex: 3, order: 2, displayName: "First");
        var second = CreateAnalysisTabContribution("second", requestedIndex: 3, order: 1, displayName: "Second");

        manager.ExtensionSlots.AnalysisTabs.Add(first);
        manager.ExtensionSlots.AnalysisTabs.Add(second);
        manager.ExtensionSlots.AnalysisTabs.ReplaceWith([first with { Order = 0 }, second]);

        AssertPageOrder(
            context.Pages,
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
        var context = CreateBuiltInContext();
        var actions = new RecordedSessionEditorActions();
        var intents = new List<RecordedSessionEditorIntent>();
        using var subscription = actions.Intents.Subscribe(intents.Add);
        var controller = CreateController(manager, context, actions);

        manager.ExtensionSlots.Pages.Add(CreatePageContribution("extension-page", requestedIndex: 1));
        var contributedPage = Assert.Single(context.Pages, page => page.DisplayName == "Extension page");

        controller.RequestRecordedSessionExtensionPageSelection("extension-page");

        var intent = Assert.IsType<RecordedSessionEditorIntent.SelectPageIndex>(Assert.Single(intents));
        Assert.Equal(context.Pages.IndexOf(contributedPage), intent.PageIndex);
        Assert.Equal(0, context.SelectedPageIndex);
    }

    [Fact]
    public void Dispose_DisposesOwnedAnalysisTabViewModels_AndLeavesBorrowedPageViewModels()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext();
        var controller = CreateController(manager, context);
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
            context.Pages.Single(page => page.DisplayName == "Extension page"));
        var analysisTab = Assert.IsType<RecordedSessionExtensionPageViewModel>(
            context.Pages.Single(page => page.DisplayName == "Analysis tab"));
        Assert.Same(pageViewModel, page.ViewModel);
        Assert.Same(analysisTabViewModel, analysisTab.ViewModel);

        controller.Dispose();
        controller.Dispose();

        Assert.Equal(0, pageViewModel.DisposeCount);
        Assert.Equal(1, analysisTabViewModel.DisposeCount);
        Assert.DoesNotContain(page, context.Pages);
        Assert.DoesNotContain(analysisTab, context.Pages);
    }

    private static RecordedSessionExtensionPagesController CreateController(
        RecordedSessionExtensionManager manager,
        RecordedSessionContext context,
        RecordedSessionEditorActions? actions = null)
    {
        return new RecordedSessionExtensionPagesController(
            manager,
            context.Pages,
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

    private static RecordedSessionContext CreateBuiltInContext(bool includeBalance = true)
    {
        var context = new RecordedSessionContext();
        context.Pages.Add(new PageViewModelBase("Signals"));
        context.Pages.Add(new PageViewModelBase("Spring"));
        context.Pages.Add(new PageViewModelBase("Strokes"));
        context.Pages.Add(new PageViewModelBase("Damping"));

        if (includeBalance)
        {
            context.Pages.Add(new PageViewModelBase("Balance"));
        }

        context.Pages.Add(new PageViewModelBase("Vibration"));
        context.Pages.Add(new PageViewModelBase("Insights"));
        return context;
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
