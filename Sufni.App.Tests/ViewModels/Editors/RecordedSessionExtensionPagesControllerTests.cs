using System.Collections.ObjectModel;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHosting.RecordedSessions;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.ExtensionHost;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;

namespace Sufni.App.Tests.ViewModels.Editors;

public class RecordedSessionExtensionPagesControllerTests
{
    [Fact]
    public void PageContributions_InsertUsingExistingRequestedIndexBehavior()
    {
        var manager = CreateManager();
        var pages = CreateBuiltInPages();
        _ = new RecordedSessionExtensionPagesController(manager, pages);

        manager.ExtensionSlots.Pages.Add(CreatePageContribution("extension-page", requestedIndex: 1));

        AssertPageOrder(
            pages,
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
        var pages = CreateBuiltInPages();
        _ = new RecordedSessionExtensionPagesController(manager, pages);

        manager.ExtensionSlots.StatisticsTabs.Add(CreateStatisticsTabContribution("statistics-tab", requestedIndex: 3));

        AssertPageOrder(
            pages,
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
        var pages = CreateBuiltInPages(includeBalance: false);
        _ = new RecordedSessionExtensionPagesController(manager, pages);

        manager.ExtensionSlots.StatisticsTabs.Add(CreateStatisticsTabContribution("statistics-tab", requestedIndex: 3));

        AssertPageOrder(
            pages,
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
        var pages = CreateBuiltInPages();
        _ = new RecordedSessionExtensionPagesController(manager, pages);

        manager.ExtensionSlots.StatisticsTabs.Add(CreateStatisticsTabContribution("statistics-tab", requestedIndex: 3));
        manager.ExtensionSlots.StatisticsTabs.Clear();

        AssertPageOrder(
            pages,
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
        var pages = CreateBuiltInPages();
        _ = new RecordedSessionExtensionPagesController(manager, pages);
        var first = CreateStatisticsTabContribution("first", requestedIndex: 3, order: 2, displayName: "First");
        var second = CreateStatisticsTabContribution("second", requestedIndex: 3, order: 1, displayName: "Second");

        manager.ExtensionSlots.StatisticsTabs.Add(first);
        manager.ExtensionSlots.StatisticsTabs.Add(second);
        manager.ExtensionSlots.StatisticsTabs.ReplaceWith([first with { Order = 0 }, second]);

        AssertPageOrder(
            pages,
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
        ObservableCollection<PageViewModelBase> pages =
        [
            new("Graph"),
            new("Spring rate"),
            new("Strokes"),
            new("Damping"),
        ];

        if (includeBalance)
        {
            pages.Add(new PageViewModelBase("Balance"));
        }

        pages.Add(new PageViewModelBase("Vibration"));
        pages.Add(new PageViewModelBase("Analysis"));
        return pages;
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
