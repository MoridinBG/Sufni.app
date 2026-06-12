using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.SessionPages;
using System.Threading.Tasks;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Owns the velocity-filter recompute flow for a recorded session: persists
/// the committed processing preference, confirms discarding unsaved changes,
/// triggers the session recompute, and reconciles the outcome back onto the
/// editor through the operation gateway and staleness reconciler.
/// </summary>
internal sealed class ProcessingPreferenceWorkflow(
    RecordedPreferenceStore preferenceStore,
    PreferencesPageViewModel preferencesPage,
    IDialogService dialogService,
    ISessionCoordinator sessionCoordinator,
    ISessionStore sessionStore,
    SessionStalenessReconciler stalenessReconciler,
    ISessionOperationGateway gateway)
{
    public async Task HandleProcessingPreferenceChangeCommittedAsync()
    {
        if (!preferenceStore.PersistenceEnabled || !gateway.IsViewLoaded)
        {
            return;
        }

        if (!preferenceStore.TryBeginProcessingPreferenceRecompute())
        {
            preferencesPage.ApplyProcessingPreferences(preferenceStore.Current.Processing);
            return;
        }

        var processing = preferencesPage.CreateProcessingPreferences();
        if (processing == preferenceStore.Current.Processing)
        {
            preferenceStore.EndProcessingPreferenceRecompute();
            return;
        }

        try
        {
            if (gateway.IsDirty)
            {
                var confirmed = await dialogService.ShowConfirmationAsync(
                    "Recompute session?",
                    "Changing the velocity filter recomputes this session and will discard unsaved changes.");
                if (!confirmed)
                {
                    preferencesPage.ApplyProcessingPreferences(preferenceStore.Current.Processing);
                    return;
                }

                if (sessionStore.Get(gateway.SessionId) is { } current)
                {
                    await gateway.ApplyPersistedSnapshotAsync(current);
                }
            }

            var previousProcessing = preferenceStore.Current.Processing;
            preferenceStore.UpdateCurrent(current => current with { Processing = processing });
            if (!await preferenceStore.PersistChangeAsync(current => current with { Processing = processing }))
            {
                preferenceStore.UpdateCurrent(current => current with { Processing = previousProcessing });
                preferencesPage.ApplyProcessingPreferences(preferenceStore.Current.Processing);
                return;
            }

            var result = await sessionCoordinator.RecomputeAsync(gateway.SessionId, gateway.BaselineUpdated);
            await stalenessReconciler.ApplyRecomputeResultAsync(result);
        }
        finally
        {
            preferenceStore.EndProcessingPreferenceRecompute();
        }
    }
}
