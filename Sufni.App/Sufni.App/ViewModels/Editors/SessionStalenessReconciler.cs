using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Coordinators;
using Sufni.App.SessionGraph;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.ViewModels.Editors;

internal sealed class SessionStalenessReconciler
{
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly ISessionStore sessionStore;
    private readonly IDialogService dialogService;
    private readonly ISessionOperationGateway gateway;
    private bool observedInitialDomain;
    private bool recomputePromptRunning;
    private string? promptedRecomputeSignature;
    private RecordedSessionDomainSnapshot? deferredDomainWhileInactive;
    private bool reportedNotRecomputableStale;

    public SessionStalenessReconciler(
        ISessionCoordinator sessionCoordinator,
        ISessionStore sessionStore,
        IDialogService dialogService,
        ISessionOperationGateway gateway)
    {
        this.sessionCoordinator = sessionCoordinator;
        this.sessionStore = sessionStore;
        this.dialogService = dialogService;
        this.gateway = gateway;
    }

    public async Task HandleDomainChangedAsync(RecordedSessionDomainSnapshot domain)
    {
        // Callers subscribe fire-and-forget; an unguarded throw here would
        // surface only as an unobserved task exception.
        try
        {
            await HandleDomainChangedCoreAsync(domain);
        }
        catch (Exception exception)
        {
            gateway.AddError($"Failed to handle a session change: {exception.Message}");
        }
    }

    private async Task HandleDomainChangedCoreAsync(RecordedSessionDomainSnapshot domain)
    {
        if (!gateway.IsViewLoaded)
        {
            return;
        }

        gateway.UpdateExtensionHostState();

        if (gateway.ShouldDeferDomainHandling())
        {
            deferredDomainWhileInactive = domain;
            return;
        }

        deferredDomainWhileInactive = null;

        var initial = !observedInitialDomain;
        observedInitialDomain = true;

        if (initial)
        {
            await HandleInitialDomainAsync(domain);
            return;
        }

        if (!domain.Staleness.IsStale)
        {
            promptedRecomputeSignature = null;
        }

        if (ShouldPromptForDerivedChange(domain.ChangeKind) && domain.Staleness.CanRecompute)
        {
            await PromptForRecomputeAsync(domain);
            return;
        }

        if (domain.Session.Updated > gateway.BaselineUpdated && !domain.Staleness.IsStale)
        {
            await ReloadFreshExternalUpdateAsync(domain);
            return;
        }

        if (domain.ChangeKind.HasFlag(DerivedChangeKind.ProcessedDataAvailabilityChanged) &&
            domain.Session.HasProcessedData &&
            !domain.Staleness.IsStale)
        {
            _ = gateway.RequestLoadAsync();
        }
    }

    public Task HandleDeferredDomainAsync()
    {
        if (!gateway.IsViewLoaded || deferredDomainWhileInactive is not { } domain)
        {
            return Task.CompletedTask;
        }

        deferredDomainWhileInactive = null;
        return HandleDomainChangedAsync(domain);
    }

    public void ResetForUnload()
    {
        observedInitialDomain = false;
        promptedRecomputeSignature = null;
        deferredDomainWhileInactive = null;
    }

    public async Task ApplyRecomputeResultAsync(SessionRecomputeResult result)
    {
        switch (result)
        {
            case SessionRecomputeResult.Recomputed recomputed:
                gateway.BaselineUpdated = recomputed.NewBaselineUpdated;
                if (sessionStore.Get(gateway.SessionId) is { } current)
                {
                    await gateway.ApplyPersistedSnapshotAsync(current);
                }

                await gateway.RequestLoadAsync();
                break;

            case SessionRecomputeResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Session changed elsewhere",
                    "This session has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    await gateway.ApplyPersistedSnapshotAsync(conflict.CurrentSnapshot);
                    await gateway.RequestLoadAsync();
                }
                break;

            case SessionRecomputeResult.NotRecomputable:
                ReportNotRecomputableStale();
                break;

            case SessionRecomputeResult.Failed failed:
                gateway.AddError($"Session could not be recomputed: {failed.ErrorMessage}");
                break;
        }
    }

    private static bool ShouldPromptForDerivedChange(DerivedChangeKind changeKind) =>
        changeKind.HasFlag(DerivedChangeKind.ProcessedDataAvailabilityChanged) ||
        changeKind.HasFlag(DerivedChangeKind.DependencyChanged) ||
        changeKind.HasFlag(DerivedChangeKind.SourceAvailabilityChanged) ||
        changeKind.HasFlag(DerivedChangeKind.FingerprintChanged);

    private async Task HandleInitialDomainAsync(RecordedSessionDomainSnapshot domain)
    {
        if (!domain.Staleness.IsStale)
        {
            return;
        }

        if (domain.Staleness.CanRecompute)
        {
            await PromptForRecomputeAsync(domain);
            return;
        }

        ReportNotRecomputableStale();
    }

    private async Task ReloadFreshExternalUpdateAsync(RecordedSessionDomainSnapshot domain)
    {
        if (gateway.IsDirty)
        {
            var reload = await dialogService.ShowConfirmationAsync(
                "Session changed elsewhere",
                "This session has been updated from another source. Discard your changes and reload?");
            if (!reload)
            {
                return;
            }
        }

        await gateway.ApplyPersistedSnapshotAsync(domain.Session);
        await gateway.RequestLoadAsync();
    }

    private void ReportNotRecomputableStale()
    {
        if (reportedNotRecomputableStale)
        {
            return;
        }

        reportedNotRecomputableStale = true;
        gateway.AddError("Session is stale and cannot be recomputed until the source recording is restored.");
    }

    private async Task PromptForRecomputeAsync(RecordedSessionDomainSnapshot domain)
    {
        if (recomputePromptRunning)
        {
            return;
        }

        var signature = RecomputePromptSignature(domain);
        if (promptedRecomputeSignature == signature)
        {
            return;
        }

        promptedRecomputeSignature = signature;
        recomputePromptRunning = true;
        try
        {
            var confirmed = await dialogService.ShowConfirmationAsync(
                RecomputePromptTitle(domain),
                RecomputePromptMessage(gateway.IsDirty));
            if (!confirmed || !gateway.IsViewLoaded)
            {
                return;
            }

            await gateway.ApplyPersistedSnapshotAsync(domain.Session);
            var result = await sessionCoordinator.RecomputeAsync(
                gateway.SessionId,
                gateway.BaselineUpdated,
                CancellationToken.None);
            await ApplyRecomputeResultAsync(result);
        }
        finally
        {
            recomputePromptRunning = false;
        }
    }

    private static string RecomputePromptTitle(RecordedSessionDomainSnapshot domain) =>
        string.IsNullOrWhiteSpace(domain.Session.Name)
            ? "Session has to be recomputed"
            : $"Session {domain.Session.Name} has to be recomputed";

    private static string RecomputePromptMessage(bool isDirty) =>
        isDirty
            ? "Recompute this session now? This will discard unsaved changes."
            : "Recompute this session now?";

    private static string RecomputePromptSignature(RecordedSessionDomainSnapshot domain) =>
        string.Join(
            "|",
            domain.Session.Id,
            domain.Session.Updated,
            domain.Session.ProcessingFingerprintJson,
            domain.CurrentFingerprint?.SchemaVersion,
            domain.CurrentFingerprint?.ProcessingVersion,
            domain.CurrentFingerprint?.SetupId,
            domain.CurrentFingerprint?.BikeId,
            domain.CurrentFingerprint?.DependencyHash,
            domain.CurrentFingerprint?.SourceHash,
            domain.Staleness.GetType().FullName);
}
