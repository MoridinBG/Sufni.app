using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.Tests.ExtensionHost;
using Sufni.App.Tests.TestSupport;

using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Extensibility.Views;
namespace Sufni.App.Tests.ViewModels.Editors;

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
                "Graph",
                "Extension page",
                "Spring rate",
                "Strokes",
                "Damping",
                "Balance",
                "Vibration",
                "Analysis",
            ]);
    }

    [Fact]
    public void StatisticsTabContributions_InsertBeforeMatchingBuiltInStatisticsPage()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext();
        _ = new RecordedSessionExtensionPagesController(manager, context);

        manager.ExtensionSlots.StatisticsTabs.Add(CreateStatisticsTabContribution("statistics-tab", requestedIndex: 3));

        AssertPageOrder(
            context.Pages,
            [
                "Graph",
                "Spring rate",
                "Strokes",
                "Damping",
                "Statistics tab",
                "Balance",
                "Vibration",
                "Analysis",
            ]);
    }

    [Fact]
    public void StatisticsTabContributions_InsertBeforeNextBuiltInStatisticsPage_WhenBalanceIsAbsent()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext(includeBalance: false);
        _ = new RecordedSessionExtensionPagesController(manager, context);

        manager.ExtensionSlots.StatisticsTabs.Add(CreateStatisticsTabContribution("statistics-tab", requestedIndex: 3));

        AssertPageOrder(
            context.Pages,
            [
                "Graph",
                "Spring rate",
                "Strokes",
                "Damping",
                "Statistics tab",
                "Vibration",
                "Analysis",
            ]);
    }

    [Fact]
    public void SlotReset_RemovesStaleStatisticsTabPages()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext();
        _ = new RecordedSessionExtensionPagesController(manager, context);

        manager.ExtensionSlots.StatisticsTabs.Add(CreateStatisticsTabContribution("statistics-tab", requestedIndex: 3));
        manager.ExtensionSlots.StatisticsTabs.Clear();

        AssertPageOrder(
            context.Pages,
            [
                "Graph",
                "Spring rate",
                "Strokes",
                "Damping",
                "Balance",
                "Vibration",
                "Analysis",
            ]);
    }

    [Fact]
    public void StatisticsTabReorder_UpdatesPageOrderDeterministically()
    {
        var manager = CreateManager();
        var context = CreateBuiltInContext();
        _ = new RecordedSessionExtensionPagesController(manager, context);
        var first = CreateStatisticsTabContribution("first", requestedIndex: 3, order: 2, displayName: "First");
        var second = CreateStatisticsTabContribution("second", requestedIndex: 3, order: 1, displayName: "Second");

        manager.ExtensionSlots.StatisticsTabs.Add(first);
        manager.ExtensionSlots.StatisticsTabs.Add(second);
        manager.ExtensionSlots.StatisticsTabs.ReplaceWith([first with { Order = 0 }, second]);

        AssertPageOrder(
            context.Pages,
            [
                "Graph",
                "Spring rate",
                "Strokes",
                "Damping",
                "First",
                "Second",
                "Balance",
                "Vibration",
                "Analysis",
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
        context.Pages.Add(new PageViewModelBase("Graph"));
        context.Pages.Add(new PageViewModelBase("Spring rate"));
        context.Pages.Add(new PageViewModelBase("Strokes"));
        context.Pages.Add(new PageViewModelBase("Damping"));

        if (includeBalance)
        {
            context.Pages.Add(new PageViewModelBase("Balance"));
        }

        context.Pages.Add(new PageViewModelBase("Vibration"));
        context.Pages.Add(new PageViewModelBase("Analysis"));
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

    private static RecordedSessionStatisticsTabContribution CreateStatisticsTabContribution(
        string contributionId,
        int requestedIndex,
        int order = 0,
        string displayName = "Statistics tab")
    {
        return new RecordedSessionStatisticsTabContribution(
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
