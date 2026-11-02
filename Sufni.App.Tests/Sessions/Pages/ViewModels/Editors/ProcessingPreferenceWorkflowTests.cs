using NSubstitute;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Pages.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Sessions.Pages.ViewModels.Editors;

public class ProcessingPreferenceWorkflowTests
{
    [Fact]
    public async Task ProcessingFailure_ProjectsTheStoredOptionAndPreservesDiagnostics()
    {
        var gateway = new TestSessionOperationGateway();
        using var store = new SessionStore(new StorePublication(new InlineUiThreadDispatcher()));
        var snapshot = TestSnapshots.Session(id: gateway.SessionId) with { VelocityFilterWindowMilliseconds = 50 };
        await store.PublishSessionsChangedAsync([snapshot]);
        var page = TestPreferencesPages.Create();
        page.VelocityFilterWindowMilliseconds = 100;
        var coordinator = Substitute.For<ISessionCoordinator>();
        coordinator.SetProcessingOptionAsync(gateway.SessionId, 100)
            .Returns(new SessionRecomputeResult.ProcessingFailed(SessionProcessingFailureReason.NoTravelData, "diagnostic"));
        var workflow = new ProcessingPreferenceWorkflow(store, page, coordinator, gateway);

        await workflow.HandleProcessingPreferenceChangeCommittedAsync();

        Assert.Equal(50, page.VelocityFilterWindowMilliseconds);
        var failure = Assert.Single(gateway.Errors);
        Assert.Equal("diagnostic", failure.Details);
        Assert.DoesNotContain("diagnostic", failure.Summary);
    }
}
