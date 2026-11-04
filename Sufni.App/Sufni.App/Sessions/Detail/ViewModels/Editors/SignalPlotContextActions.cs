using Sufni.App.Sessions.Signals.ViewModels.Editors;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Views.Plots;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class SignalPlotContextActions : IDisposable
{
    private readonly RecordedSessionEditorActions editorActions;
    private readonly SegmentPickerViewModel segmentPicker;
    private readonly CancellationToken lifetime;
    private readonly IDisposable stateSubscription;
    private RecordedSessionEditorState currentState = RecordedSessionEditorState.CreateInitial();
    public IRelayCommand<TelemetryPlotContextMenuContext?> SetAnalysisRangeStartCommand { get; }
    public IRelayCommand<TelemetryPlotContextMenuContext?> SetAnalysisRangeEndCommand { get; }
    public IRelayCommand<TelemetryPlotContextMenuContext?> ClearAnalysisRangeFromContextCommand { get; }
    public IAsyncRelayCommand<TelemetryPlotContextMenuContext?> SaveSelectionAsSegmentCommand { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId { get; }

    public SignalPlotContextActions(IObservable<RecordedSessionEditorState> state, RecordedSessionEditorActions editorActions,
        SessionTimelineLinkViewModel timeline, SegmentPickerViewModel segmentPicker,
        TimelineAlignmentController alignment, SessionWindowEditController windowEdits, CancellationToken lifetime)
    {
        this.editorActions = editorActions;
        this.segmentPicker = segmentPicker;
        this.lifetime = lifetime;
        var autozoom = new SignalAutozoomController(timeline);
        SetAnalysisRangeStartCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            SetAnalysisRangeStartFromPlotContext,
            CanSetAnalysisRangeFromPlotContext);
        SetAnalysisRangeEndCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            SetAnalysisRangeEndFromPlotContext,
            CanSetAnalysisRangeFromPlotContext);
        ClearAnalysisRangeFromContextCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            ClearAnalysisRangeFromPlotContext,
            CanClearAnalysisRangeFromPlotContext);
        SaveSelectionAsSegmentCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            SaveSelectionAsSegmentFromPlotContextAsync,
            CanSaveSelectionAsSegmentFromPlotContext);
        SignalPlotContextMenuActionsBySignalRowId = CreateSignalPlotContextMenuActionsBySignalRowId(
            autozoom.ActionsBySignalRowId, alignment, windowEdits);
        stateSubscription = state.Subscribe(value =>
        {
            currentState = value;
            SetAnalysisRangeStartCommand.NotifyCanExecuteChanged();
            SetAnalysisRangeEndCommand.NotifyCanExecuteChanged();
            ClearAnalysisRangeFromContextCommand.NotifyCanExecuteChanged();
            SaveSelectionAsSegmentCommand.NotifyCanExecuteChanged();
        });
    }

    public void Dispose() => stateSubscription.Dispose();

    private IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> CreateSignalPlotContextMenuActionsBySignalRowId(
        IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> baseActions,
        TimelineAlignmentController alignment,
        SessionWindowEditController windowEdits)
    {
        var setAnalysisRangeStart = new TelemetryPlotContextMenuAction(
            "analysis-range-set-start",
            "Set analysis start here",
            SetAnalysisRangeStartCommand);
        var setAnalysisRangeEnd = new TelemetryPlotContextMenuAction(
            "analysis-range-set-end",
            "Set analysis end here",
            SetAnalysisRangeEndCommand);
        var clearAnalysisRange = new TelemetryPlotContextMenuAction(
            "analysis-range-clear",
            "Clear analysis range",
            ClearAnalysisRangeFromContextCommand);
        var markGpsEvent = new TelemetryPlotContextMenuAction(
            "gps-mark-gps-event",
            "Mark GPS event here",
            alignment.MarkGpsEventCommand);
        var markGpsTelemetryEvent = new TelemetryPlotContextMenuAction(
            "gps-mark-telemetry-event",
            "Mark telemetry event here",
            alignment.MarkGpsTelemetryEventCommand);
        var cancelGpsTimelineAlignment = new TelemetryPlotContextMenuAction(
            "gps-cancel-alignment",
            "Cancel GPS alignment",
            alignment.CancelGpsTimelineAlignmentCommand);
        var markVideoEvent = new TelemetryPlotContextMenuAction(
            "video-mark-video-event",
            "Mark video event here",
            alignment.MarkVideoEventCommand);
        var markVideoTelemetryEvent = new TelemetryPlotContextMenuAction(
            "video-mark-telemetry-event",
            "Mark telemetry event here",
            alignment.MarkVideoTelemetryEventCommand);
        var cancelVideoAlignment = new TelemetryPlotContextMenuAction(
            "video-cancel-alignment",
            "Cancel video alignment",
            alignment.CancelVideoAlignmentCommand);
        var trimFromBeginning = new TelemetryPlotContextMenuAction(
            "session-trim-from-beginning",
            "Trim from beginning",
            windowEdits.TrimFromBeginningCommand);
        var trimUntilEnd = new TelemetryPlotContextMenuAction(
            "session-trim-until-end",
            "Trim until end",
            windowEdits.TrimUntilEndCommand);
        var split = new TelemetryPlotContextMenuAction(
            "session-split-here",
            "Split here",
            windowEdits.SplitCommand);
        var undoTrim = new TelemetryPlotContextMenuAction(
            "session-undo-trim",
            "Undo trim",
            windowEdits.UndoTrimCommand);
        var saveSelectionAsSegment = new TelemetryPlotContextMenuAction(
            "save-current-selection",
            "Save current selection",
            SaveSelectionAsSegmentCommand);
        TelemetryPlotContextMenuAction[] actions =
        [
            setAnalysisRangeStart,
            setAnalysisRangeEnd,
            clearAnalysisRange,
            markGpsEvent,
            markGpsTelemetryEvent,
            cancelGpsTimelineAlignment,
            markVideoEvent,
            markVideoTelemetryEvent,
            cancelVideoAlignment,
            trimFromBeginning,
            trimUntilEnd,
            split,
            undoTrim,
            saveSelectionAsSegment,
        ];

        return new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>
        {
            [SignalRowIds.Travel] = AppendContextMenuActions(baseActions, SignalRowIds.Travel, actions),
            [SignalRowIds.Velocity] = AppendContextMenuActions(baseActions, SignalRowIds.Velocity, actions),
            [SignalRowIds.Imu] = AppendContextMenuActions(baseActions, SignalRowIds.Imu, actions),
            [SignalRowIds.PitchRoll] = AppendContextMenuActions(baseActions, SignalRowIds.PitchRoll, actions),
            [SignalRowIds.Speed] = AppendContextMenuActions(baseActions, SignalRowIds.Speed, actions),
            [SignalRowIds.Elevation] = AppendContextMenuActions(baseActions, SignalRowIds.Elevation, actions),
        };
    }

    private static IReadOnlyList<TelemetryPlotContextMenuAction> AppendContextMenuActions(
        IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> baseActions,
        string rowId,
        params TelemetryPlotContextMenuAction[] actions)
    {
        return baseActions.TryGetValue(rowId, out var rowActions)
            ? [.. rowActions, .. actions]
            : actions;
    }

    internal static bool IsTelemetryPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return context is { ClickSeconds: >= 0 } &&
               double.IsFinite(context.ClickSeconds) &&
               context.RowId is SignalRowIds.Travel or
                   SignalRowIds.Velocity or
                   SignalRowIds.Imu or
                   SignalRowIds.PitchRoll or
                   SignalRowIds.Speed or
                   SignalRowIds.Elevation;
    }

    private bool CanSetAnalysisRangeFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return currentState.TelemetryData is not null &&
               (!lifetime.IsCancellationRequested && SignalPlotContextActions.IsTelemetryPlotContext(context));
    }

    private bool CanClearAnalysisRangeFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return (currentState.Intent.AnalysisRange is not null ||
                currentState.Intent.PendingAnalysisRangeBoundary is not null) &&
               (!lifetime.IsCancellationRequested && SignalPlotContextActions.IsTelemetryPlotContext(context));
    }

    private void SetAnalysisRangeStartFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanSetAnalysisRangeFromPlotContext(context))
        {
            return;
        }

        editorActions.SetAnalysisRangeStartBoundary(context!.ClickSeconds);
    }

    private void SetAnalysisRangeEndFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanSetAnalysisRangeFromPlotContext(context))
        {
            return;
        }

        editorActions.SetAnalysisRangeEndBoundary(context!.ClickSeconds);
    }

    private void ClearAnalysisRangeFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanClearAnalysisRangeFromPlotContext(context))
        {
            return;
        }

        editorActions.ClearAnalysisRange();
    }

    private bool CanSaveSelectionAsSegmentFromPlotContext(TelemetryPlotContextMenuContext? context) =>
        currentState.TelemetryData is not null &&
        (!lifetime.IsCancellationRequested && SignalPlotContextActions.IsTelemetryPlotContext(context)) &&
        context!.IsClickInsideAnalysisRange;

    private Task SaveSelectionAsSegmentFromPlotContextAsync(TelemetryPlotContextMenuContext? context) =>
        CanSaveSelectionAsSegmentFromPlotContext(context)
            ? segmentPicker.SaveSelectionAsync(context!.AnalysisRange!.Value)
            : Task.CompletedTask;

}
