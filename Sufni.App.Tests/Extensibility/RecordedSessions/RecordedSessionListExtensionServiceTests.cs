using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;

using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.Extensibility.RecordedSessions;

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

    [Fact]
    public void ContributionsChanged_Raises_WhenProviderInvalidatesListContributions()
    {
        var provider = new TestInvalidatingContributionProvider();
        var service = new RecordedSessionListExtensionService([provider]);
        var raisedCount = 0;
        service.ContributionsChanged += (_, _) => raisedCount++;

        provider.RaiseContributionsChanged();

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public void Constructor_RejectsDuplicateProviderExtensionIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RecordedSessionListExtensionService(
            [
                new TestContributionProvider("duplicate", indicatorOrder: 10, actionOrder: 20),
                new TestContributionProvider("duplicate", indicatorOrder: 30, actionOrder: 40),
            ]));

        Assert.Contains("duplicate", exception.Message);
    }

    [Fact]
    public void CreateContributions_RejectsDuplicateContributionIdsAcrossIndicatorsAndActions()
    {
        var service = new RecordedSessionListExtensionService([new DuplicateContributionProvider()]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateIndicators(CreateSummary()));

        Assert.Contains("duplicate", exception.Message);
        Assert.Contains("extension", exception.Message);
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
        public string ExtensionId => id;

        public IReadOnlyList<RecordedSessionListIndicatorContribution> CreateIndicators(RecordedSessionSummary summary)
        {
            return
            [
                new RecordedSessionListIndicatorContribution(
                    ExtensionId,
                    $"{id}-{summary.Name}-indicator",
                    indicatorOrder,
                    new TestContributionViewModel()),
            ];
        }

        public IReadOnlyList<RecordedSessionListActionContribution> CreateActions(RecordedSessionSummary summary)
        {
            return
            [
                new RecordedSessionListActionContribution(
                    ExtensionId,
                    $"{id}-{summary.Name}-action",
                    actionOrder,
                    new TestContributionViewModel()),
            ];
        }
    }

    private sealed class TestInvalidatingContributionProvider :
        IRecordedSessionListContributionProvider,
        IRecordedSessionListContributionChangeSource
    {
        public event EventHandler? ContributionsChanged;

        public string ExtensionId => "invalidating";

        public IReadOnlyList<RecordedSessionListIndicatorContribution> CreateIndicators(RecordedSessionSummary summary) => [];

        public IReadOnlyList<RecordedSessionListActionContribution> CreateActions(RecordedSessionSummary summary) => [];

        public void RaiseContributionsChanged()
        {
            ContributionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class DuplicateContributionProvider : IRecordedSessionListContributionProvider
    {
        public string ExtensionId => "extension";

        public IReadOnlyList<RecordedSessionListIndicatorContribution> CreateIndicators(RecordedSessionSummary summary)
        {
            return
            [
                new RecordedSessionListIndicatorContribution(
                    ExtensionId,
                    "duplicate",
                    Order: 10,
                    new TestContributionViewModel()),
            ];
        }

        public IReadOnlyList<RecordedSessionListActionContribution> CreateActions(RecordedSessionSummary summary)
        {
            return
            [
                new RecordedSessionListActionContribution(
                    ExtensionId,
                    "duplicate",
                    Order: 20,
                    new TestContributionViewModel()),
            ];
        }
    }
}
