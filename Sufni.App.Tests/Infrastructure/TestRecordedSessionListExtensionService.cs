using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;
using Sufni.App.Tests.ExtensionHost;

namespace Sufni.App.Tests.Infrastructure;

internal sealed class TestRecordedSessionListExtensionService : IRecordedSessionListExtensionService
{
    public event EventHandler? ContributionsChanged;

    public int ContributionRevision { get; set; }

    public IReadOnlyList<RecordedSessionListIndicatorContribution> CreateIndicators(RecordedSessionSummary summary)
    {
        return
        [
            new RecordedSessionListIndicatorContribution(
                "extension",
                $"{ContributionIdPrefix(summary)}-indicator",
                Order: 0,
                new TestContributionViewModel
                {
                    Content = new TextBlock { Name = "SessionListIndicator", Text = "Indicator" },
                }),
        ];
    }

    public IReadOnlyList<RecordedSessionListActionContribution> CreateActions(RecordedSessionSummary summary)
    {
        return
        [
            new RecordedSessionListActionContribution(
                "extension",
                $"{ContributionIdPrefix(summary)}-action",
                Order: 0,
                new TestContributionViewModel
                {
                    Content = new TextBlock { Name = "SessionListAction", Text = "Action" },
                }),
        ];
    }

    public void RaiseContributionsChanged()
    {
        ContributionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string ContributionIdPrefix(RecordedSessionSummary summary) =>
        ContributionRevision == 0
            ? summary.Name
            : $"{summary.Name}-{ContributionRevision}";
}
