using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Pages.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.Sessions.Pages.ViewModels.Editors;

public class ProcessingPreferenceWorkflowTests
{
    [Fact]
    public async Task ProcessingFailure_RestoresSavedOptionAndSlider_AndPreservesDiagnostics()
    {
        var gateway = new TestSessionOperationGateway();
        var persisted = SessionPreferences.Default;
        var preferences = Substitute.For<ISessionPreferences>();
        preferences.UpdateRecordedAsync(gateway.SessionId, Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(call =>
            {
                persisted = call.Arg<Func<SessionPreferences, SessionPreferences>>()(persisted);
                return Task.CompletedTask;
            });
        var store = new RecordedPreferenceStore(preferences, () => gateway.SessionId, _ => { });
        var page = new PreferencesPageViewModel { VelocityFilterWindowMilliseconds = 100 };
        var coordinator = Substitute.For<ISessionCoordinator>();
        coordinator.RequestRecomputeAsync(gateway.SessionId, RecomputeReason.ProcessingPreferenceChanged)
            .Returns(new SessionRecomputeResult.ProcessingFailed(SessionProcessingFailureReason.NoTravelData, "diagnostic"));
        var workflow = new ProcessingPreferenceWorkflow(store, page, coordinator, gateway);

        await workflow.HandleProcessingPreferenceChangeCommittedAsync();

        Assert.Equal(SessionPreferences.Default.Processing, persisted.Processing);
        Assert.Equal(persisted.Processing, page.CreateProcessingPreferences());
        Assert.Equal(persisted.Processing, store.Current.Processing);
        var failure = Assert.Single(gateway.Errors);
        Assert.Equal("diagnostic", failure.Details);
        Assert.DoesNotContain("diagnostic", failure.Summary);
    }
}
