using NSubstitute;
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
        _ = new RecordedSessionExtensionPagesController(manager, context);

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
        _ = new RecordedSessionExtensionPagesController(manager, context);

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
        _ = new RecordedSessionExtensionPagesController(manager, context);

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
    public void SlotReset_RemovesStaleAnalysisTabPages()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext();
        _ = new RecordedSessionExtensionPagesController(manager, context);

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
    public void AnalysisTabReorder_UpdatesPageOrderDeterministically()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext();
        _ = new RecordedSessionExtensionPagesController(manager, context);
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
    public void RequestPageSelection_SetsSelectedPageIndexOnContext()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext();
        var controller = new RecordedSessionExtensionPagesController(manager, context);

        manager.ExtensionSlots.Pages.Add(CreatePageContribution("extension-page", requestedIndex: 1));
        var contributedPage = Assert.Single(context.Pages, page => page.DisplayName == "Extension page");

        controller.RequestRecordedSessionExtensionPageSelection("extension-page");

        Assert.Equal(context.Pages.IndexOf(contributedPage), context.SelectedPageIndex);
        Assert.Same(contributedPage, context.SelectedPage);
        Assert.Equal("Extension page", context.SelectedPageDisplayName);
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
        int requestedIndex)
    {
        return new RecordedSessionPageContribution(
            "extension",
            contributionId,
            Order: 0,
            "Extension page",
            new TestContributionViewModel(),
            requestedIndex);
    }

    private static RecordedSessionAnalysisTabContribution CreateAnalysisTabContribution(
        string contributionId,
        int requestedIndex,
        int order = 0,
        string displayName = "Analysis tab")
    {
        return new RecordedSessionAnalysisTabContribution(
            "extension",
            contributionId,
            order,
            displayName,
            requestedIndex,
            new TestContributionViewModel());
    }

    private static void AssertPageOrder(
        IReadOnlyList<PageViewModelBase> pages,
        IReadOnlyList<string> expectedDisplayNames)
    {
        Assert.Equal(expectedDisplayNames, pages.Select(page => page.DisplayName));
    }
}
