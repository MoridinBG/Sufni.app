using Sufni.App.Coordinators;
using Sufni.App.SessionGraph;
using Sufni.App.ViewModels.SessionPages;
using System.Threading.Tasks;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Owns the velocity-filter recompute flow for a recorded session: persists the
/// committed processing preference, publishes it into the processing-option cache
/// so staleness re-evaluates against the new option, then requests a recompute
/// through the engine. The committed slider value is authoritative; a recompute
/// displaced by a newer request needs no rollback, and the engine's store upsert
/// drives the editor refresh on success.
/// </summary>
internal sealed class ProcessingPreferenceWorkflow(
    RecordedPreferenceStore preferenceStore,
    PreferencesPageViewModel preferencesPage,
    ISessionCoordinator sessionCoordinator,
    IRecordedSessionProcessingOptionCache processingOptionCache,
    ISessionOperationGateway gateway)
{
    public async Task HandleProcessingPreferenceChangeCommittedAsync()
    {
        if (!preferenceStore.PersistenceEnabled || !gateway.IsViewLoaded)
        {
            return;
        }

        var processing = preferencesPage.CreateProcessingPreferences();
        if (processing == preferenceStore.Current.Processing)
        {
            return;
        }

        var previousProcessing = preferenceStore.Current.Processing;

        // Restores everything — persisted option, graph cache, and the slider — to
        // the pre-change value when the recompute cannot complete.
        async Task RevertAsync()
        {
            preferenceStore.UpdateCurrent(current => current with { Processing = previousProcessing });
            await preferenceStore.PersistChangeAsync(current => current with { Processing = previousProcessing });
            processingOptionCache.Set(gateway.SessionId, previousProcessing.ToTelemetryProcessingOptions());
            preferencesPage.ApplyProcessingPreferences(previousProcessing);
        }

        preferenceStore.UpdateCurrent(current => current with { Processing = processing });
        if (!await preferenceStore.PersistChangeAsync(current => current with { Processing = processing }))
        {
            preferenceStore.UpdateCurrent(current => current with { Processing = previousProcessing });
            preferencesPage.ApplyProcessingPreferences(preferenceStore.Current.Processing);
            return;
        }

        // Publish the new option into the graph cache so staleness evaluates against
        // it; otherwise the graph would compare the just-recomputed fingerprint
        // (new option) against a stale cached option and report the session stale.
        processingOptionCache.Set(gateway.SessionId, processing.ToTelemetryProcessingOptions());

        // The request flips the engine's IsActive(id) true synchronously, so the
        // staleness prompter suppresses the stale emission this same option change
        // produces in the graph — the local case never double-prompts.
        var result = await sessionCoordinator.RequestRecomputeAsync(
            gateway.SessionId,
            RecomputeReason.ProcessingPreferenceChanged);
        switch (result)
        {
            case SessionRecomputeResult.Failed failed:
                gateway.AddError($"Session could not be recomputed: {failed.ErrorMessage}");
                await RevertAsync();
                break;

            case SessionRecomputeResult.NotRecomputable:
                gateway.AddError("Session is stale and cannot be recomputed until the source recording is restored.");
                await RevertAsync();
                break;

            // Recomputed: the engine's store upsert drives the editor through the
            // session watch reaction; the committed slider value is authoritative.
            // Superseded: a newer explicit request owns the result; no rollback.
        }
    }
}
