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
    private readonly Func<Guid> getSessionId;
    private readonly Func<long> getBaselineUpdated;
    private readonly Action<long> setBaselineUpdated;
    private readonly Func<bool> isDirty;
    private readonly Func<bool> isViewLoaded;
    private readonly Func<bool> shouldDeferDomainHandling;
    private readonly Func<SessionSnapshot, Task> applyPersistedSnapshotAsync;
    private readonly Func<Task> requestLoadAsync;
    private readonly Action updateHostState;
    private readonly Action<string> reportError;
    private bool observedInitialDomain;
    private bool recomputePromptRunning;
    private string? promptedRecomputeSignature;
    private RecordedSessionDomainSnapshot? deferredDomainWhileInactive;
    private bool reportedNotRecomputableStale;

    public SessionStalenessReconciler(
        ISessionCoordinator sessionCoordinator,
        ISessionStore sessionStore,
        IDialogService dialogService,
        Func<Guid> getSessionId,
        Func<long> getBaselineUpdated,
        Action<long> setBaselineUpdated,
        Func<bool> isDirty,
        Func<bool> isViewLoaded,
        Func<bool> shouldDeferDomainHandling,
        Func<SessionSnapshot, Task> applyPersistedSnapshotAsync,
        Func<Task> requestLoadAsync,
        Action updateHostState,
        Action<string> reportError)
    {
        this.sessionCoordinator = sessionCoordinator;
        this.sessionStore = sessionStore;
        this.dialogService = dialogService;
        this.getSessionId = getSessionId;
        this.getBaselineUpdated = getBaselineUpdated;
        this.setBaselineUpdated = setBaselineUpdated;
        this.isDirty = isDirty;
        this.isViewLoaded = isViewLoaded;
        this.shouldDeferDomainHandling = shouldDeferDomainHandling;
        this.applyPersistedSnapshotAsync = applyPersistedSnapshotAsync;
        this.requestLoadAsync = requestLoadAsync;
        this.updateHostState = updateHostState;
        this.reportError = reportError;
    }

    public async Task HandleDomainChangedAsync(RecordedSessionDomainSnapshot domain)
    {
        if (!isViewLoaded())
        {
            return;
        }

        updateHostState();

        if (shouldDeferDomainHandling())
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

        if (domain.Session.Updated > getBaselineUpdated() && !domain.Staleness.IsStale)
        {
            await ReloadFreshExternalUpdateAsync(domain);
            return;
        }

        if (domain.ChangeKind.HasFlag(DerivedChangeKind.ProcessedDataAvailabilityChanged) &&
            domain.Session.HasProcessedData &&
            !domain.Staleness.IsStale)
        {
            _ = requestLoadAsync();
        }
    }

    public Task HandleDeferredDomainAsync()
    {
        if (!isViewLoaded() || deferredDomainWhileInactive is not { } domain)
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
                setBaselineUpdated(recomputed.NewBaselineUpdated);
                if (sessionStore.Get(getSessionId()) is { } current)
                {
                    await applyPersistedSnapshotAsync(current);
                }

                await requestLoadAsync();
                break;

            case SessionRecomputeResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Session changed elsewhere",
                    "This session has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    await applyPersistedSnapshotAsync(conflict.CurrentSnapshot);
                    await requestLoadAsync();
                }
                break;

            case SessionRecomputeResult.NotRecomputable:
                ReportNotRecomputableStale();
                break;

            case SessionRecomputeResult.Failed failed:
                reportError($"Session could not be recomputed: {failed.ErrorMessage}");
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
        if (isDirty())
        {
            var reload = await dialogService.ShowConfirmationAsync(
                "Session changed elsewhere",
                "This session has been updated from another source. Discard your changes and reload?");
            if (!reload)
            {
                return;
            }
        }

        await applyPersistedSnapshotAsync(domain.Session);
        await requestLoadAsync();
    }

    private void ReportNotRecomputableStale()
    {
        if (reportedNotRecomputableStale)
        {
            return;
        }

        reportedNotRecomputableStale = true;
        reportError("Session is stale and cannot be recomputed until the source recording is restored.");
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
                RecomputePromptMessage(isDirty()));
            if (!confirmed || !isViewLoaded())
            {
                return;
            }

            await applyPersistedSnapshotAsync(domain.Session);
            var result = await sessionCoordinator.RecomputeAsync(
                getSessionId(),
                getBaselineUpdated(),
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
