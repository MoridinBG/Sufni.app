using Sufni.App.Shared.Models;
using System;
using System.Globalization;
using System.Threading.Tasks;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
namespace Sufni.App.Sessions.Pages.ViewModels.Editors;

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
    // Recompute-specific prompt choice ids. The dialog service stays generic; this
    // reconciler owns what the buttons mean.
    private const string RecomputeThisChoiceId = "recompute-this";
    private const string RecomputeAllChoiceId = "recompute-all";
    private const string CancelChoiceId = "cancel";

    private readonly ISessionRecomputeEngine recomputeEngine;
    private readonly IDialogService dialogService;
    private readonly ISessionOperationGateway gateway;
    private bool recomputePromptRunning;
    private string? promptedRecomputeSignature;
    private bool reportedUnavailableInputs;

    public SessionStalenessReconciler(
        ISessionRecomputeEngine recomputeEngine,
        IDialogService dialogService,
        ISessionOperationGateway gateway)
    {
        this.recomputeEngine = recomputeEngine;
        this.dialogService = dialogService;
        this.gateway = gateway;
    }

    public async Task HandleStalenessAsync(RecordedSessionDomainSnapshot domain)
    {
        // Suppress the prompt for a recompute the user just triggered: the request
        // flips engine.IsActive(id) true synchronously before any await, and both
        // the projection emission and the request run on the UI thread.
        if (recomputeEngine.IsActive(gateway.SessionId))
        {
            return;
        }

        if (domain.Staleness is SessionStaleness.MissingRawSource)
        {
            ReportUnavailableInputs(domain.Staleness);
            return;
        }

        if (!domain.Staleness.IsStale)
        {
            promptedRecomputeSignature = null;
            return;
        }

        if (domain.Staleness.CanRecompute)
        {
            await PromptForRecomputeAsync(domain);
            return;
        }

        ReportUnavailableInputs(domain.Staleness);
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
            var choice = await dialogService.ShowChoiceAsync(
                RecomputePromptTitle(domain),
                RecomputePromptMessage(gateway.IsDirty),
                new[]
                {
                    new DialogChoice(CancelChoiceId, "Cancel"),
                    new DialogChoice(RecomputeAllChoiceId, "Recompute all"),
                    // Recomputing just the prompted session is the primary/default action.
                    new DialogChoice(RecomputeThisChoiceId, "Recompute", IsDefault: true)
                });
            if (!gateway.IsViewLoaded)
            {
                return;
            }

            switch (choice)
            {
                case RecomputeAllChoiceId:
                    await RecomputeAllAsync();
                    return;

                case RecomputeThisChoiceId:
                    break;

                default:
                    // Cancel or dismissed: nothing to recompute.
                    return;
            }

            var result = await recomputeEngine.RequestRecomputeAsync(gateway.SessionId);
            switch (result)
            {
                case SessionRecomputeResult.NotRecomputable unavailable:
                    ReportUnavailableInputs(unavailable.Reason);
                    break;

                case SessionRecomputeResult.ProcessingFailed failed:
                    gateway.AddError(FailurePresentation.From(failed.Reason, failed.Details));
                    break;
                case SessionRecomputeResult.Missing:
                    gateway.Notify("The session is no longer available.");
                    break;
                case SessionRecomputeResult.Failed failed:
                    gateway.AddError(FailurePresentation.From(failed.Reason, failed.Details));
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

    private async Task RecomputeAllAsync()
    {
        // Run the parallel bulk recompute behind a modal progress dialog, relaying
        // the engine's completed/total progress to the dialog's bar and status.
        var summary = await dialogService.ShowProgressAsync(
            "Recomputing all sessions",
            progress =>
            {
                var relay = new Progress<SessionRecomputeAllProgress>(report =>
                    progress.Report(new DialogProgress(
                        report.Total == 0 ? 1 : (double)report.Completed / report.Total,
                        $"{report.Completed} of {report.Total} sessions")));
                return recomputeEngine.RequestRecomputeAllAsync(relay);
            });

        // Skipped (not-recomputable) sessions are an expected outcome of a bulk
        // recompute, so only hard failures are surfaced to the user.
        if (gateway.IsViewLoaded && summary.Failed > 0)
        {
            gateway.AddError(new FailurePresentation($"{summary.Failed} of {summary.Total} sessions could not be recomputed."));
        }
    }

    private void ReportUnavailableInputs(SessionStaleness reason)
    {
        if (reportedUnavailableInputs)
        {
            return;
        }

        reportedUnavailableInputs = true;
        gateway.AddError(new FailurePresentation(reason is SessionStaleness.MissingDependencies
            ? "Session cannot be recomputed until its missing setup or bike is restored."
            : "The source recording is unavailable. Existing processed data can still be viewed."));
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
            WindowSignature(domain.CurrentFingerprint?.DerivationWindow),
            domain.Staleness.GetType().FullName);

    private static string? WindowSignature(RecordedSessionDerivationWindow? window) =>
        window is null
            ? null
            : string.Join(
                ",",
                window.SourceSessionId,
                window.StartSeconds.ToString("R", CultureInfo.InvariantCulture),
                window.EndSeconds?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty);
}
