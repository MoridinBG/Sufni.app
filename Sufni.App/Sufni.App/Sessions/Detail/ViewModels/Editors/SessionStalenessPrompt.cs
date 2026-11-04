using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Shared.Models;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class SessionStalenessPrompt : IDisposable
{
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly IDialogService dialogService;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly Func<bool> isDirty;
    private readonly CancellationToken lifetime;
    private readonly Action<FailurePresentation> reportError;
    private readonly Action<string> notify;
    private readonly IDisposable stateSubscription;
    private RecordedSessionEditorState currentState = RecordedSessionEditorState.CreateInitial();
    private SessionStalenessReconciliation.Required? promptedRecompute;
    private bool recomputePromptRunning;
    private bool reportedUnavailableInputs;

    public SessionStalenessPrompt(IObservable<RecordedSessionEditorState> state, ISessionCoordinator sessionCoordinator,
        IDialogService dialogService, IUiThreadDispatcher uiThreadDispatcher, Func<bool> isDirty,
        CancellationToken lifetime, Action<FailurePresentation> reportError, Action<string> notify)
    {
        this.sessionCoordinator = sessionCoordinator;
        this.dialogService = dialogService;
        this.uiThreadDispatcher = uiThreadDispatcher;
        this.isDirty = isDirty;
        this.lifetime = lifetime;
        this.reportError = reportError;
        this.notify = notify;
        stateSubscription = state.Subscribe(value => currentState = value);
    }

    public void Dispose() => stateSubscription.Dispose();

    public async Task ReconcileStalenessAsync(RecordedSessionDomainSnapshot domain)
    {
        if (lifetime.IsCancellationRequested) return;
        var result = await sessionCoordinator.ReconcileStalenessAsync(domain);
        if (lifetime.IsCancellationRequested) return;
        switch (result)
        {
            case SessionStalenessReconciliation.Current:
                promptedRecompute = null;
                return;
            case SessionStalenessReconciliation.Unavailable unavailable:
                ReportUnavailableInputs(unavailable.Reason);
                return;
            case SessionStalenessReconciliation.Required required:
                if (recomputePromptRunning || promptedRecompute == required) return;
                promptedRecompute = required;
                break;
            default:
                return;
        }

        recomputePromptRunning = true;
        try
        {
            var choice = await dialogService.ShowChoiceAsync(
                string.IsNullOrWhiteSpace(domain.Session.Name) ? "Session has to be recomputed" : $"Session {domain.Session.Name} has to be recomputed",
                isDirty() ? "Recompute this session now? Your unsaved changes will be kept." : "Recompute this session now?",
                [new DialogChoice("cancel", "Cancel"), new DialogChoice("recompute-all", "Recompute all"),
                    new DialogChoice("recompute-this", "Recompute", IsDefault: true)]);
            if (lifetime.IsCancellationRequested) return;
            var current = currentState.Domain ?? domain;
            if (choice == "recompute-all")
            {
                await dialogService.ShowProgressAsync("Recomputing all sessions", progress =>
                    sessionCoordinator.ReconcileStalenessAsync(current, SessionRecomputeChoice.AllSessions,
                        new UiThreadProgress<SessionRecomputeAllProgress>(uiThreadDispatcher, report =>
                        {
                            if (!lifetime.IsCancellationRequested)
                                progress.Report(new DialogProgress(report.Total == 0 ? 1 : (double)report.Completed / report.Total,
                                    $"{report.Completed} of {report.Total} sessions"));
                        })));
                return;
            }
            if (choice != "recompute-this") return;
            result = await sessionCoordinator.ReconcileStalenessAsync(current, SessionRecomputeChoice.ThisSession);
            if (lifetime.IsCancellationRequested) return;
            switch (result)
            {
                case SessionStalenessReconciliation.Unavailable unavailable:
                    ReportUnavailableInputs(unavailable.Reason);
                    break;
                case SessionStalenessReconciliation.Recomputed { Result: SessionRecomputeResult.NotRecomputable unavailable }:
                    ReportUnavailableInputs(unavailable.Reason);
                    break;
                case SessionStalenessReconciliation.Recomputed { Result: SessionRecomputeResult.ProcessingFailed failed }:
                    reportError(FailurePresentation.From(failed.Reason, failed.Details));
                    break;
                case SessionStalenessReconciliation.Recomputed { Result: SessionRecomputeResult.Failed failed }:
                    reportError(FailurePresentation.From(failed.Reason, failed.Details));
                    break;
                case SessionStalenessReconciliation.Recomputed { Result: SessionRecomputeResult.Missing }:
                    notify("The session is no longer available.");
                    break;
            }
        }
        finally
        {
            recomputePromptRunning = false;
        }
    }

    private void ReportUnavailableInputs(SessionStaleness reason)
    {
        if (reportedUnavailableInputs) return;
        reportedUnavailableInputs = true;
        reportError(new FailurePresentation(reason is SessionStaleness.MissingDependencies
            ? "Session cannot be recomputed until its missing setup or bike is restored."
            : "The source recording is unavailable. Existing processed data can still be viewed."));
    }

}
