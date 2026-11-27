using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.SensorFindings.ViewModels;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

/// <summary>
/// Offers the undecided sensor findings of the loaded generation in a dialog while the tab is active and the generation
/// is current. Each set of findings is offered once per editor; the recompute after a Drop yields the same set.
/// </summary>
internal sealed class SensorFindingsPrompt : IDisposable
{
    private static readonly DialogOptions Options = new(
        Title: "Sensor findings",
        Width: 560,
        Height: 420,
        MinWidth: 420,
        MinHeight: 320,
        CanResize: true);

    private readonly Guid sessionId;
    private readonly ISessionStore sessionStore;
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly IDialogService dialogs;
    private readonly IUiThreadDispatcher ui;
    private readonly Func<bool> active;
    private readonly Action<FailurePresentation> report;
    private readonly Action<string> notify;
    private readonly CancellationToken lifetime;
    private readonly IDisposable subscription;
    private TaskCompletionSource? activation;
    private ImmutableArray<SensorFinding> offered = [];
    private bool offering;

    public SensorFindingsPrompt(Guid sessionId, IObservable<RecordedSessionEditorState> states, ISessionStore sessionStore,
        ISessionCoordinator sessionCoordinator, IDialogService dialogs, IUiThreadDispatcher ui, Func<bool> active,
        Action<FailurePresentation> report, Action<string> notify, CancellationToken lifetime)
    {
        this.sessionId = sessionId;
        this.sessionStore = sessionStore;
        this.sessionCoordinator = sessionCoordinator;
        this.dialogs = dialogs;
        this.ui = ui;
        this.active = active;
        this.report = report;
        this.notify = notify;
        this.lifetime = lifetime;
        subscription = states.Subscribe(Update);
    }

    public void Activate() => activation?.TrySetResult();

    public void Dispose() => subscription.Dispose();

    private void Update(RecordedSessionEditorState state)
    {
        if (lifetime.IsCancellationRequested || offering) return;
        if (state.Domain is not { Staleness: SessionStaleness.Current } || state.Session is not { } session ||
            state.TelemetryData is not { } telemetry || telemetry.SensorFindings.SequenceEqual(offered))
            return;

        offered = telemetry.SensorFindings;
        if (session.SensorDecisions.Undecided(offered).IsEmpty) return;
        _ = OfferAsync(offered);
    }

    private async Task OfferAsync(ImmutableArray<SensorFinding> findings)
    {
        offering = true;
        try
        {
            while (!active())
            {
                activation = new(TaskCreationOptions.RunContinuationsAsynchronously);
                try { await activation.Task.WaitAsync(lifetime); }
                finally { activation = null; }
            }

            using var content = new SensorFindingsViewModel(sessionId, findings, sessionStore, sessionCoordinator, ui, report, notify);
            await dialogs.ShowContentDialogAsync(content, Options).WaitAsync(lifetime);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            offering = false;
        }
    }
}
