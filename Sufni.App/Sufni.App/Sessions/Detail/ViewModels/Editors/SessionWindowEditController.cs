using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Shell.Coordinators;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class SessionWindowEditController : IDisposable
{
    private readonly Guid sessionId;
    private readonly ISessionEditingCoordinator sessionEditingCoordinator;
    private readonly IShellCoordinator shell;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly CancellationToken lifetime;
    private readonly Action<FailurePresentation> reportError;
    private readonly Action<string> notify;
    private readonly IDisposable stateSubscription;
    private RecordedSessionEditorState currentState = RecordedSessionEditorState.CreateInitial();
    public IAsyncRelayCommand<TelemetryPlotContextMenuContext?> TrimFromBeginningCommand { get; }
    public IAsyncRelayCommand<TelemetryPlotContextMenuContext?> TrimUntilEndCommand { get; }
    public IAsyncRelayCommand<TelemetryPlotContextMenuContext?> SplitCommand { get; }
    public IAsyncRelayCommand<TelemetryPlotContextMenuContext?> UndoTrimCommand { get; }

    public SessionWindowEditController(Guid sessionId, IObservable<RecordedSessionEditorState> state,
        ISessionEditingCoordinator sessionEditingCoordinator, IShellCoordinator shell,
        IUiThreadDispatcher uiThreadDispatcher, CancellationToken lifetime,
        Action<FailurePresentation> reportError, Action<string> notify)
    {
        this.sessionId = sessionId;
        this.sessionEditingCoordinator = sessionEditingCoordinator;
        this.shell = shell;
        this.uiThreadDispatcher = uiThreadDispatcher;
        this.lifetime = lifetime;
        this.reportError = reportError;
        this.notify = notify;
        TrimFromBeginningCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            TrimFromBeginningFromPlotContextAsync,
            CanTrimFromBeginningFromPlotContext);
        TrimUntilEndCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            TrimUntilEndFromPlotContextAsync,
            CanTrimUntilEndFromPlotContext);
        SplitCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            SplitFromPlotContextAsync,
            CanSplitFromPlotContext);
        UndoTrimCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            UndoTrimFromPlotContextAsync,
            CanUndoTrimFromPlotContext);
        TrimFromBeginningCommand.PropertyChanged += OnCommandChanged;
        TrimUntilEndCommand.PropertyChanged += OnCommandChanged;
        SplitCommand.PropertyChanged += OnCommandChanged;
        UndoTrimCommand.PropertyChanged += OnCommandChanged;
        stateSubscription = state.Subscribe(value => { currentState = value; RefreshCommands(); });
    }

    private void OnCommandChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IAsyncRelayCommand.IsRunning) && !lifetime.IsCancellationRequested)
            RefreshCommands();
    }

    private void RefreshCommands()
    {
        TrimFromBeginningCommand.NotifyCanExecuteChanged();
        TrimUntilEndCommand.NotifyCanExecuteChanged();
        SplitCommand.NotifyCanExecuteChanged();
        UndoTrimCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        stateSubscription.Dispose();
        TrimFromBeginningCommand.PropertyChanged -= OnCommandChanged;
        TrimUntilEndCommand.PropertyChanged -= OnCommandChanged;
        SplitCommand.PropertyChanged -= OnCommandChanged;
        UndoTrimCommand.PropertyChanged -= OnCommandChanged;
    }

    private bool IsSessionEditRunning =>
        TrimFromBeginningCommand.IsRunning ||
        TrimUntilEndCommand.IsRunning ||
        SplitCommand.IsRunning ||
        UndoTrimCommand.IsRunning;

    private bool CanEditWindowAt(TelemetryPlotContextMenuContext? context, SessionWindowEditKind kind) =>
        !IsSessionEditRunning && (!lifetime.IsCancellationRequested && SignalPlotContextActions.IsTelemetryPlotContext(context)) && currentState.TelemetryData is { } telemetry &&
        SessionWindowEditPolicy.CanEditAt(kind, context!.ClickSeconds, context.DurationSeconds, telemetry.Metadata.SampleRate);

    private bool CanTrimFromBeginningFromPlotContext(TelemetryPlotContextMenuContext? context) =>
        CanEditWindowAt(context, SessionWindowEditKind.TrimFromBeginning);

    private bool CanTrimUntilEndFromPlotContext(TelemetryPlotContextMenuContext? context) =>
        CanEditWindowAt(context, SessionWindowEditKind.TrimUntilEnd);

    private bool CanSplitFromPlotContext(TelemetryPlotContextMenuContext? context) =>
        CanEditWindowAt(context, SessionWindowEditKind.Split);

    private bool CanUndoTrimFromPlotContext(TelemetryPlotContextMenuContext? context) =>
        !IsSessionEditRunning &&
        (!lifetime.IsCancellationRequested && SignalPlotContextActions.IsTelemetryPlotContext(context)) &&
        currentState.Session is { CanUndoTrim: true };

    private Task TrimFromBeginningFromPlotContextAsync(TelemetryPlotContextMenuContext? context) =>
        CanTrimFromBeginningFromPlotContext(context)
            ? RunSessionEditAsync(
                "Session trimmed.",
                () => sessionEditingCoordinator.TrimFromBeginningAsync(sessionId, context!.ClickSeconds))
            : Task.CompletedTask;

    private Task TrimUntilEndFromPlotContextAsync(TelemetryPlotContextMenuContext? context) =>
        CanTrimUntilEndFromPlotContext(context)
            ? RunSessionEditAsync(
                "Session trimmed.",
                () => sessionEditingCoordinator.TrimUntilEndAsync(sessionId, context!.ClickSeconds))
            : Task.CompletedTask;

    private Task SplitFromPlotContextAsync(TelemetryPlotContextMenuContext? context) =>
        CanSplitFromPlotContext(context)
            ? RunSessionEditAsync(
                "Session split.",
                () => sessionEditingCoordinator.SplitAsync(sessionId, context!.ClickSeconds,
                    new UiThreadProgress<SessionEditProgress>(uiThreadDispatcher,
                        progress => { if (!lifetime.IsCancellationRequested) shell.OpenEditor(new EditorKey.Session(progress.CreatedSessionId), background: true); })))
            : Task.CompletedTask;

    private Task UndoTrimFromPlotContextAsync(TelemetryPlotContextMenuContext? context) =>
        CanUndoTrimFromPlotContext(context)
            ? RunSessionEditAsync(
                "Session trim restored.",
                () => sessionEditingCoordinator.UndoTrimAsync(sessionId))
            : Task.CompletedTask;

    private async Task RunSessionEditAsync(string appliedMessage, Func<Task<SessionEditResult>> edit)
    {
        var result = await edit();
        if (lifetime.IsCancellationRequested) return;
        switch (result)
        {
            case SessionEditResult.Applied { RecomputeFailure: SessionRecomputeResult.Failed failed }:
                ShowRecomputeFailure(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SessionEditResult.Applied { RecomputeFailure: SessionRecomputeResult.ProcessingFailed failed }:
                ShowRecomputeFailure(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SessionEditResult.Applied { RecomputeFailure: SessionRecomputeResult.Missing }:
                notify("The session is no longer available.");
                break;
            case SessionEditResult.Applied { RecomputeFailure: SessionRecomputeResult.NotRecomputable }:
                notify("The edit was applied, but the session cannot be recomputed until its source and dependencies are available.");
                break;
            case SessionEditResult.Applied:
                notify(appliedMessage);
                break;
            case SessionEditResult.Missing:
                notify("The session is no longer available.");
                break;
            case SessionEditResult.Failed failed:
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
        void ShowRecomputeFailure(FailurePresentation failure) =>
            reportError(new FailurePresentation($"The edit was applied. {failure.Summary}", failure.Details));
    }

}
