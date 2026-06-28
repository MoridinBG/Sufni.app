using System.Threading.Tasks;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.SessionGraph;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Forward-only staleness prompter for the opened session. When the session is
/// stale and recomputable it confirms with the user and requests a recompute
/// through the engine, surfacing only the unrecomputable / failed outcomes. A
/// recompute the user just triggered suppresses the prompt (engine liveness),
/// and on success the engine's store upsert — not this prompter — drives the
/// editor refresh through the session-detail watch reaction.
/// </summary>
internal sealed class SessionStalenessReconciler
{
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly IDialogService dialogService;
    private readonly ISessionOperationGateway gateway;
    private bool recomputePromptRunning;
    private string? promptedRecomputeSignature;
    private bool reportedNotRecomputableStale;

    public SessionStalenessReconciler(
        ISessionCoordinator sessionCoordinator,
        IDialogService dialogService,
        ISessionOperationGateway gateway)
    {
        this.sessionCoordinator = sessionCoordinator;
        this.dialogService = dialogService;
        this.gateway = gateway;
    }

    public async Task HandleStalenessAsync(RecordedSessionDomainSnapshot domain, RecomputeReason reason)
    {
        // Suppress the prompt for a recompute the user just triggered: the request
        // flips engine.IsActive(id) true synchronously before any await, and both
        // the graph emission and the request run on the UI thread.
        if (sessionCoordinator.IsRecomputeActive(gateway.SessionId))
        {
            return;
        }

        if (!domain.Staleness.IsStale)
        {
            promptedRecomputeSignature = null;
            return;
        }

        if (domain.Staleness.CanRecompute)
        {
            await PromptForRecomputeAsync(domain, reason);
            return;
        }

        ReportNotRecomputableStale();
    }

    public void ResetForUnload()
    {
        promptedRecomputeSignature = null;
        reportedNotRecomputableStale = false;
    }

    private async Task PromptForRecomputeAsync(RecordedSessionDomainSnapshot domain, RecomputeReason reason)
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

            var result = await sessionCoordinator.RequestRecomputeAsync(gateway.SessionId, reason);
            switch (result)
            {
                case SessionRecomputeResult.NotRecomputable:
                    ReportNotRecomputableStale();
                    break;

                case SessionRecomputeResult.Failed failed:
                    gateway.AddError($"Session could not be recomputed: {failed.ErrorMessage}");
                    break;

                // Recomputed: the engine's store upsert drives the editor through the
                // session-detail watch reaction. Superseded: a newer explicit request
                // owns the result.
            }
        }
        finally
        {
            recomputePromptRunning = false;
        }
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

    private static string RecomputePromptTitle(RecordedSessionDomainSnapshot domain) =>
        string.IsNullOrWhiteSpace(domain.Session.Name)
            ? "Session has to be recomputed"
            : $"Session {domain.Session.Name} has to be recomputed";

    private static string RecomputePromptMessage(bool isDirty) =>
        isDirty
            ? "Recompute this session now? Your unsaved changes will be kept."
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
            domain.CurrentFingerprint?.TrackProjectionVersion,
            domain.CurrentFingerprint?.DependencyHash,
            domain.CurrentFingerprint?.SourceHash,
            domain.CurrentFingerprint?.VelocityFilterWindowMilliseconds,
            domain.Staleness.GetType().FullName);
}
