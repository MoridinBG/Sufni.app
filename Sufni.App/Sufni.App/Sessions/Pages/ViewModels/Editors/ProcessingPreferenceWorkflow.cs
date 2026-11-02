using System.Threading.Tasks;
using Sufni.App.Shared.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Sessions.Pages.ViewModels.Editors;

/// <summary>Forwards committed filter edits and projects their outcome.</summary>
internal sealed class ProcessingPreferenceWorkflow(
    ISessionStore sessionStore,
    PreferencesPageViewModel preferencesPage,
    ISessionCoordinator sessionCoordinator,
    ISessionOperationGateway gateway)
{
    public async Task HandleProcessingPreferenceChangeCommittedAsync()
    {
        if (!gateway.IsViewLoaded || sessionStore.Get(gateway.SessionId) is not { } previous) return;
        var options = preferencesPage.CreateProcessingOptions();
        if (options == previous.ProcessingOptions) return;

        var result = await sessionCoordinator.SetProcessingOptionAsync(
            gateway.SessionId, options.ClampedVelocityFilterWindowMilliseconds);
        if (!gateway.IsViewLoaded) return;
        if (result is not SessionRecomputeResult.Superseded)
            preferencesPage.ApplyProcessingOptions((sessionStore.Get(gateway.SessionId) ?? previous).ProcessingOptions);
        switch (result)
        {
            case SessionRecomputeResult.ProcessingFailed failed:
                gateway.AddError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SessionRecomputeResult.Failed failed:
                gateway.AddError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SessionRecomputeResult.Missing:
                gateway.Notify("The session is no longer available.");
                break;
            case SessionRecomputeResult.NotRecomputable:
                gateway.AddError(new FailurePresentation("Session is stale and cannot be recomputed until the source recording is restored."));
                break;
        }
    }
}
