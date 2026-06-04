using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.SessionGraph;

namespace Sufni.App.Tests.ExtensionHost;

public class RecordedSessionListExtensionServiceTests
{
    [Fact]
    public void CreateContributions_ReturnsEmptyLists_WhenNoProvidersAreRegistered()
    {
        var service = new RecordedSessionListExtensionService();
        var summary = CreateSummary();

        Assert.Empty(service.CreateIndicators(summary));
        Assert.Empty(service.CreateActions(summary));
    }

    [Fact]
    public void CreateContributions_AggregatesProvidersAndSortsByOrder()
    {
        var service = new RecordedSessionListExtensionService(
        [
            new TestContributionProvider("first", indicatorOrder: 20, actionOrder: 30),
            new TestContributionProvider("second", indicatorOrder: 10, actionOrder: 20),
        ]);
        var summary = CreateSummary(name: "session");

        var indicators = service.CreateIndicators(summary);
        var actions = service.CreateActions(summary);

        Assert.Equal(["second-session-indicator", "first-session-indicator"], indicators.Select(indicator => indicator.ContributionId));
        Assert.Equal(["second-session-action", "first-session-action"], actions.Select(action => action.ContributionId));
    }

    private static RecordedSessionSummary CreateSummary(string name = "session") => new(
        Guid.NewGuid(),
        Updated: 1,
        name,
        Description: string.Empty,
        Timestamp: null,
        HasProcessedData: true,
        new SessionStaleness.Current());

    private sealed class TestContributionProvider(
        string id,
        int indicatorOrder,
        int actionOrder) : IRecordedSessionListContributionProvider
    {
        public IReadOnlyList<RecordedSessionListIndicatorContribution> CreateIndicators(RecordedSessionSummary summary)
        {
            return
            [
                new RecordedSessionListIndicatorContribution(
                    "extension",
                    $"{id}-{summary.Name}-indicator",
                    indicatorOrder,
                    new object()),
            ];
        }

        public IReadOnlyList<RecordedSessionListActionContribution> CreateActions(RecordedSessionSummary summary)
        {
            return
            [
                new RecordedSessionListActionContribution(
                    "extension",
                    $"{id}-{summary.Name}-action",
                    actionOrder,
                    new object()),
            ];
        }
    }
}
