using Sufni.App.Shared.Models;

namespace Sufni.App.Tests.Shared.Models;

public class FailurePresentationTests
{
    [Fact]
    public void StartupFailure_KeepsDiagnosticTextOutOfTheUserFacingSummary()
    {
        var first = FailurePresentation.From(StartupFailureReason.Database, "database file /private/example.db is corrupt");
        var second = FailurePresentation.From(StartupFailureReason.Database, "disk error 123");

        Assert.Equal(first.Summary, second.Summary);
        Assert.DoesNotContain(first.Details!, first.Summary);
        Assert.DoesNotContain(second.Details!, second.Summary);
        Assert.NotEqual(first.Details, second.Details);
    }
}
