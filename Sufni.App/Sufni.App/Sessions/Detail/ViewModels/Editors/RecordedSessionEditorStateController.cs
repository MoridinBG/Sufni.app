using Sufni.App.Sessions.Analysis.Services;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Analysis.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Store;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed record RecordedSessionEditorStateInputs(
    IObservable<RecordedSessionEditorIntent> Intents,
    IObservable<int> PageCounts,
    IObservable<SessionPreferences> PreferenceReplays,
    IObservable<RecordedSessionLoadPresentation> LoadPresentations,
    IObservable<SessionOperationPresentationState> OperationStates,
    IObservable<SessionDampingPercentages> DampingPercentages,
    IObservable<SessionInsightsResult> SessionInsights,
    IObservable<AnalysisAvailabilityResult> AnalysisAvailability,
    IObservable<SelectionHighlightsAnalysisResult> SelectionHighlights,
    IObservable<IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>> SignalPlotContextMenuActions,
    IObservable<RecordedSessionDomainSnapshot> DomainStates);

internal sealed class RecordedSessionEditorStateController : IDisposable
{
    private readonly IDisposable connection;
    private bool disposed;

    public RecordedSessionEditorStateController(RecordedSessionEditorStateInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputs.Intents);
        ArgumentNullException.ThrowIfNull(inputs.PageCounts);
        ArgumentNullException.ThrowIfNull(inputs.PreferenceReplays);
        ArgumentNullException.ThrowIfNull(inputs.LoadPresentations);
        ArgumentNullException.ThrowIfNull(inputs.OperationStates);
        ArgumentNullException.ThrowIfNull(inputs.DampingPercentages);
        ArgumentNullException.ThrowIfNull(inputs.SessionInsights);
        ArgumentNullException.ThrowIfNull(inputs.AnalysisAvailability);
        ArgumentNullException.ThrowIfNull(inputs.SelectionHighlights);
        ArgumentNullException.ThrowIfNull(inputs.SignalPlotContextMenuActions);
        ArgumentNullException.ThrowIfNull(inputs.DomainStates);

        var initial = RecordedSessionEditorState.CreateInitial();
        var updates = Observable.Merge(
            Updates(inputs.Intents, ReduceIntent),
            Updates(inputs.PageCounts, static (current, count) => current with
            {
                PageCount = count,
                Editor = current.Editor with
                {
                    Intent = current.Editor.Intent with
                    {
                        SelectedPageIndex = RecordedSessionPageSelection.ClampSelectedPageIndex(current.Editor.Intent.SelectedPageIndex, count),
                    },
                },
            }),
            EditorUpdates(inputs.PreferenceReplays, ApplyPreferences),
            Updates(inputs.LoadPresentations, ApplyLoad),
            EditorUpdates(inputs.OperationStates, static (state, operation) => state with
            {
                Presentation = state.Presentation with { OperationState = operation },
            }),
            EditorUpdates(inputs.DampingPercentages, static (state, percentages) => state with
            {
                Presentation = state.Presentation with { DampingPercentages = percentages },
            }),
            EditorUpdates(inputs.SessionInsights, static (state, insights) => state with
            {
                Presentation = state.Presentation with { SessionInsights = insights },
            }),
            EditorUpdates(inputs.AnalysisAvailability, static (state, availability) => state with
            {
                Presentation = state.Presentation with
                {
                    Analysis = RecordedSessionPresentationDeriver.CreateAnalysisPresentation(availability, state.Intent.AnalysisRange),
                },
            }),
            EditorUpdates(inputs.SelectionHighlights, static (state, highlights) =>
                ApplySelection(state, state.AnalysisSelection with { HighlightRanges = highlights.Ranges })),
            EditorUpdates(inputs.SignalPlotContextMenuActions, static (state, actions) => state with
            {
                Presentation = state.Presentation with { SignalPlotContextMenuActionsBySignalRowId = actions },
            }),
            EditorUpdates(inputs.DomainStates, ApplyDomain));
        var replayingState = updates
            // Effects may emit another intent while observing a state. Finish this publication first.
            .ObserveOn(CurrentThreadScheduler.Instance)
            .Scan(new Reduction(initial, 0, AnalysisRangeIntentState.Empty), static (current, update) =>
            {
                var next = update(current);
                return next with { Editor = InvalidateAnalysisResults(current.Editor, next.Editor) };
            })
            .Select(static current => current.Editor)
            .StartWith(initial)
            .DistinctUntilChanged()
            .Replay(1);

        State = replayingState.AsObservable();
        connection = replayingState.Connect();
    }

    private static IObservable<Func<Reduction, Reduction>> Updates<T>(
        IObservable<T> source, Func<Reduction, T, Reduction> reduce) =>
        source.Select(value => new Func<Reduction, Reduction>(current => reduce(current, value)));

    private static IObservable<Func<Reduction, Reduction>> EditorUpdates<T>(
        IObservable<T> source, Func<RecordedSessionEditorState, T, RecordedSessionEditorState> reduce) =>
        Updates(source, (current, value) => current with { Editor = reduce(current.Editor, value) });

    private static RecordedSessionEditorState InvalidateAnalysisResults(RecordedSessionEditorState previous, RecordedSessionEditorState next)
    {
        var scopeChanged = previous.TelemetryData != next.TelemetryData || previous.Intent.AnalysisRange != next.Intent.AnalysisRange;
        if (scopeChanged)
            next = ApplySelection(next, next.AnalysisSelection with { HighlightRanges = [] });
        var dampingChanged = scopeChanged ||
            previous.Preferences.Analysis.VelocityAverageMode != next.Preferences.Analysis.VelocityAverageMode ||
            previous.Intent.DampingSpeedCutoffs != next.Intent.DampingSpeedCutoffs;
        var insightsChanged = dampingChanged || previous.Preferences.Analysis != next.Preferences.Analysis;
        return next with
        {
            Presentation = next.Presentation with
            {
                Analysis = scopeChanged ? CreateAnalysisPresentationFromLoadPresentation(next.Load) : next.Presentation.Analysis,
                DampingPercentages = dampingChanged ? SessionDampingPercentages.Empty : next.Presentation.DampingPercentages,
                SessionInsights = insightsChanged ? SessionInsightsResult.Hidden : next.Presentation.SessionInsights,
            },
        };
    }

    private static Reduction ReduceIntent(Reduction current, RecordedSessionEditorIntent intent)
    {
        var state = current.Editor;
        if (RecordedSessionEditorIntentReducers.CreatePreferenceUpdate(intent) is { } preferenceUpdate)
            return current with { Editor = ApplyPreferences(state, preferenceUpdate(state.Preferences)) };

        if (CreateAnalysisRangeUpdate(intent, state.TelemetryData) is { } rangeUpdate)
        {
            var requested = rangeUpdate(current.RequestedRange);
            var range = ClampAnalysisRange(requested.AnalysisRange, state.TelemetryData);
            var updated = state with
            {
                Intent = state.Intent with
                {
                    AnalysisRange = range,
                    PendingAnalysisRangeBoundary = requested.PendingAnalysisRangeBoundary,
                },
            };
            return current with { Editor = updated, RequestedRange = requested };
        }

        if (CreateAnalysisSelectionUpdate(intent, state.TelemetryData) is { } selectionUpdate)
            return current with { Editor = ApplySelection(state, selectionUpdate(state.AnalysisSelection)) };

        return current with
        {
            Editor = intent switch
            {
                RecordedSessionEditorIntent.SelectPageIndex select => state with
                {
                    Intent = state.Intent with
                    {
                        SelectedPageIndex = RecordedSessionPageSelection.ClampSelectedPageIndex(select.PageIndex, current.PageCount),
                    },
                },
                RecordedSessionEditorIntent.SetDampingSpeedCutoffs set => state with
                {
                    Intent = state.Intent with { DampingSpeedCutoffs = set.Cutoffs },
                    Presentation = state.Presentation with
                    {
                        PlotDampingSpeedCutoffs = set.UpdatePlots ? set.Cutoffs : state.Presentation.PlotDampingSpeedCutoffs,
                    },
                },
                RecordedSessionEditorIntent.SetSignalPresentation set => state with
                {
                    Presentation = state.Presentation with
                    {
                        Signals = CreateSignalPresentationFromLoadPresentation(state.Load, set.State),
                    },
                },
                _ => state,
            },
        };
    }

    private static RecordedSessionEditorState ApplyDomain(RecordedSessionEditorState state, RecordedSessionDomainSnapshot domain)
    {
        var previousBike = state.Domain?.Bike;
        var cutoffsChanged = previousBike?.Id != domain.Bike?.Id || previousBike?.DampingSpeedCutoffs != domain.Bike?.DampingSpeedCutoffs;
        var cutoffs = domain.Bike?.DampingSpeedCutoffs ?? DampingSpeedCutoffs.Default;
        return state with
        {
            Domain = domain,
            Session = domain.Session,
            Intent = state.Intent with { DampingSpeedCutoffs = cutoffsChanged ? cutoffs : state.Intent.DampingSpeedCutoffs },
            Presentation = state.Presentation with
            {
                CanEditDampingSpeedCutoffs = domain.Bike is not null,
                PlotDampingSpeedCutoffs = cutoffsChanged ? cutoffs : state.Presentation.PlotDampingSpeedCutoffs,
            },
        };
    }

    private static Reduction ApplyLoad(Reduction current, RecordedSessionLoadPresentation load)
    {
        var state = current.Editor;
        var loaded = CreateLoadedDataFromLoadPresentation(load);
        var requested = state.TelemetryData is not null && state.TelemetryData != loaded.TelemetryData
            ? AnalysisRangeIntentState.Empty : current.RequestedRange;
        var range = ClampAnalysisRange(requested.AnalysisRange, loaded.TelemetryData);
        var updated = state with
        {
            Load = load,
            Session = state.Domain?.Session ?? loaded.Session ?? state.Session,
            TelemetryData = loaded.TelemetryData,
            FullTrackPoints = loaded.FullTrackPoints,
            TrackPoints = loaded.TrackPoints,
            TrackTimelineContext = loaded.TrackTimelineContext,
            Intent = state.Intent with { AnalysisRange = range, PendingAnalysisRangeBoundary = requested.PendingAnalysisRangeBoundary },
            Presentation = state.Presentation with
            {
                ScreenState = CreateScreenStateFromLoadPresentation(load),
                MapState = CreateMapStateFromLoadPresentation(load),
                SignalAvailability = CreateSignalAvailabilityFromLoadPresentation(load),
                Signals = CreateSignalPresentationFromLoadPresentation(load, state.Presentation.Signals),
                Analysis = load is RecordedSessionLoadPresentation.Loaded && state.TelemetryData == loaded.TelemetryData && state.Intent.AnalysisRange == range
                    ? state.Presentation.Analysis : CreateAnalysisPresentationFromLoadPresentation(load),
            },
        };
        return current with { Editor = updated, RequestedRange = requested };
    }

    private static RecordedSessionEditorState ApplyPreferences(RecordedSessionEditorState state, SessionPreferences preferences)
    {
        var updated = state with { Preferences = preferences };
        return state.Preferences.Analysis.VelocityAverageMode == preferences.Analysis.VelocityAverageMode
            ? updated
            : ApplySelection(updated, ClearDampingRangeSelections(state.AnalysisSelection));
    }

    private static RecordedSessionEditorState ApplySelection(RecordedSessionEditorState state, AnalysisSelectionState selection) =>
        state.AnalysisSelection == selection ? state : state with
        {
            AnalysisSelection = selection,
            Presentation = state.Presentation with
            {
                Signals = ApplyAnalysisSelectionOverlayDefaults(state.Presentation.Signals, selection),
            },
        };

    public IObservable<RecordedSessionEditorState> State { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        connection.Dispose();
    }

    private static RecordedSessionLoadedData CreateLoadedDataFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        if (load is not RecordedSessionLoadPresentation.Loaded loaded)
        {
            return new RecordedSessionLoadedData(
                GetSessionSnapshot(load),
                TelemetryData: null,
                FullTrackPoints: null,
                TrackPoints: null,
                TrackTimelineContext: null);
        }

        var telemetry = loaded.Data.TelemetryPresentation;
        var session = loaded.Session;
        return new RecordedSessionLoadedData(
            Session: session,
            TelemetryData: telemetry.TelemetryData,
            FullTrackPoints: telemetry.FullTrackPoints,
            TrackPoints: telemetry.TrackPoints,
            TrackTimelineContext: CreateTrackTimelineContext(
                telemetry.TelemetryData,
                telemetry.TrackPoints,
                session));
    }

    private static SessionSnapshot? GetSessionSnapshot(RecordedSessionLoadPresentation load)
    {
        return load switch
        {
            RecordedSessionLoadPresentation.Empty empty => empty.Session,
            RecordedSessionLoadPresentation.Loading loading => loading.Session,
            RecordedSessionLoadPresentation.Loaded loaded => loaded.Session,
            RecordedSessionLoadPresentation.IncompleteLocalData incomplete => incomplete.Session,
            RecordedSessionLoadPresentation.Failed failed => failed.Session,
            _ => null,
        };
    }

    private static TrackTimeRange? CreateTrackTimelineContext(
        TelemetryData? telemetry,
        IReadOnlyList<TrackPoint>? trackPoints,
        SessionSnapshot? session)
    {
        return telemetry is null
            ? null
            : TrackPointSeries.BuildTimelineContext(
                trackPoints,
                SessionTrackTimeline.FromSessionWindow(telemetry.Metadata.Timestamp, session?.GpsOffsetSeconds ?? 0, session?.WindowStartSeconds ?? 0).TrackOriginSeconds,
                telemetry.Metadata.Duration);
    }

    private static SessionScreenPresentationState CreateScreenStateFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        return load switch
        {
            RecordedSessionLoadPresentation.Loading loading =>
                SessionScreenPresentationState.Loading(
                    loading.Progress.Message,
                    loading.Progress.ProgressFraction),
            RecordedSessionLoadPresentation.IncompleteLocalData incomplete =>
                SessionScreenPresentationState.IncompleteLocalData(
                    FormatIncompleteLocalDataMessage(incomplete.Missing)),
            RecordedSessionLoadPresentation.Failed failed =>
                SessionScreenPresentationState.Error($"Could not load session data: {failed.ErrorMessage}"),
            _ => SessionScreenPresentationState.Ready,
        };
    }

    private static SurfacePresentationState CreateMapStateFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        return load switch
        {
            RecordedSessionLoadPresentation.Loading loading => loading.MapExpected
                ? SurfacePresentationState.Loading("Loading map data.")
                : SurfacePresentationState.Hidden,
            RecordedSessionLoadPresentation.Loaded loaded =>
                RecordedSessionPresentationDeriver.CreateMapState(
                    loaded.Data.TelemetryPresentation.TrackPoints,
                    loaded.Data.TelemetryPresentation.FullTrackId is not null),
            _ => SurfacePresentationState.Hidden,
        };
    }

    private static RecordedSignalAvailabilityState CreateSignalAvailabilityFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        return load is RecordedSessionLoadPresentation.Loaded loaded
            ? RecordedSessionPresentationDeriver.CreateSignalAvailability(
                loaded.Data.TelemetryPresentation.TelemetryData,
                loaded.Data.TelemetryPresentation.TrackPoints)
            : RecordedSessionPresentationDeriver.CreateHiddenSignalAvailabilityState();
    }

    private static RecordedSignalPresentationState CreateSignalPresentationFromLoadPresentation(
        RecordedSessionLoadPresentation load,
        RecordedSignalPresentationState explicitState)
    {
        if (load is RecordedSessionLoadPresentation.Empty)
        {
            return explicitState;
        }

        return PreserveSignalPresentationOverlays(
            CreateSignalPresentationFromLoadPresentation(load),
            explicitState);
    }

    private static RecordedSignalPresentationState CreateSignalPresentationFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        return load switch
        {
            RecordedSessionLoadPresentation.Loading loading =>
                RecordedSessionPresentationDeriver.CreateLoadingSignalPresentation(loading.MapExpected),
            RecordedSessionLoadPresentation.Loaded loaded =>
                RecordedSessionPresentationDeriver.CreateSignalPresentation(
                    loaded.Data.TelemetryPresentation.TelemetryData,
                    loaded.Data.TelemetryPresentation.TrackPoints),
            _ => RecordedSessionPresentationDeriver.CreateHiddenSignalPresentationState(),
        };
    }

    private static RecordedSignalPresentationState PreserveSignalPresentationOverlays(
        RecordedSignalPresentationState derived,
        RecordedSignalPresentationState explicitState)
    {
        return derived with
        {
            ShowAirtime = explicitState.ShowAirtime,
            ShowVelocityAirtime = explicitState.ShowVelocityAirtime,
            ShowImuAirtime = explicitState.ShowImuAirtime,
            ShowPitchRollAirtime = explicitState.ShowPitchRollAirtime,
            ShowSpeedAirtime = explicitState.ShowSpeedAirtime,
            ShowElevationAirtime = explicitState.ShowElevationAirtime,
            ShowAnalysisSelection = explicitState.ShowAnalysisSelection,
            ShowVelocityAnalysisSelection = explicitState.ShowVelocityAnalysisSelection,
            ShowImuAnalysisSelection = explicitState.ShowImuAnalysisSelection,
            ShowPitchRollAnalysisSelection = explicitState.ShowPitchRollAnalysisSelection,
            ShowSpeedAnalysisSelection = explicitState.ShowSpeedAnalysisSelection,
            ShowElevationAnalysisSelection = explicitState.ShowElevationAnalysisSelection,
            TravelHeaderActions = explicitState.TravelHeaderActions,
            VelocityHeaderActions = explicitState.VelocityHeaderActions,
            ImuHeaderActions = explicitState.ImuHeaderActions,
            PitchRollHeaderActions = explicitState.PitchRollHeaderActions,
            SpeedHeaderActions = explicitState.SpeedHeaderActions,
            ElevationHeaderActions = explicitState.ElevationHeaderActions,
        };
    }

    private static RecordedAnalysisPresentationState CreateAnalysisPresentationFromLoadPresentation(
        RecordedSessionLoadPresentation load)
    {
        if (load is RecordedSessionLoadPresentation.Loading)
        {
            return RecordedSessionPresentationDeriver.CreateLoadingAnalysisPresentationState();
        }

        if (load is not RecordedSessionLoadPresentation.Loaded loaded)
        {
            return RecordedSessionPresentationDeriver.CreateHiddenAnalysisPresentationState();
        }

        return RecordedSessionPresentationDeriver.CreatePendingAnalysisPresentation(
            loaded.Data.TelemetryPresentation.TelemetryData);
    }

    private static string FormatIncompleteLocalDataMessage(MissingSessionData missing)
    {
        var missingParts = new List<string>();
        if (missing.ProcessedTelemetryBlob)
        {
            missingParts.Add("processed telemetry");
        }

        if (missing.RecordedSourceMissing)
        {
            missingParts.Add("recorded source");
        }

        return missingParts.Count == 0
            ? "Local session data is incomplete. Run sync and try again."
            : $"Local session data is incomplete: {string.Join(", ", missingParts)}. Run sync and try again.";
    }

    private static Func<AnalysisRangeIntentState, AnalysisRangeIntentState>? CreateAnalysisRangeUpdate(
        RecordedSessionEditorIntent intent,
        TelemetryData? telemetryData)
    {
        return intent switch
        {
            RecordedSessionEditorIntent.SetAnalysisRange set =>
                _ => new AnalysisRangeIntentState(set.Range, PendingAnalysisRangeBoundary: null),
            RecordedSessionEditorIntent.ClearAnalysisRange =>
                _ => AnalysisRangeIntentState.Empty,
            RecordedSessionEditorIntent.SetAnalysisRangeBoundary set =>
                current => ApplyAnalysisRangeBoundary(current, set.Seconds, telemetryData, AnalysisRangeBoundaryMode.Nearest),
            RecordedSessionEditorIntent.SetAnalysisRangeStartBoundary set =>
                current => ApplyAnalysisRangeBoundary(current, set.Seconds, telemetryData, AnalysisRangeBoundaryMode.Start),
            RecordedSessionEditorIntent.SetAnalysisRangeEndBoundary set =>
                current => ApplyAnalysisRangeBoundary(current, set.Seconds, telemetryData, AnalysisRangeBoundaryMode.End),
            _ => null,
        };
    }

    private static AnalysisRangeIntentState ApplyAnalysisRangeBoundary(
        AnalysisRangeIntentState current,
        double boundarySeconds,
        TelemetryData? telemetryData,
        AnalysisRangeBoundaryMode mode)
    {
        if (telemetryData is null ||
            !TelemetryTimeRange.TryClampBoundary(
                boundarySeconds,
                telemetryData.Metadata.Duration,
                out var clampedBoundarySeconds))
        {
            return current with { PendingAnalysisRangeBoundary = null };
        }

        var range = ClampAnalysisRange(current.AnalysisRange, telemetryData);
        if (range is { } currentRange)
        {
            return mode switch
            {
                AnalysisRangeBoundaryMode.Start => CreateAnalysisRangeState(current, clampedBoundarySeconds, currentRange.EndSeconds, telemetryData),
                AnalysisRangeBoundaryMode.End => CreateAnalysisRangeState(current, currentRange.StartSeconds, clampedBoundarySeconds, telemetryData),
                _ when Math.Abs(clampedBoundarySeconds - currentRange.StartSeconds) <=
                       Math.Abs(clampedBoundarySeconds - currentRange.EndSeconds) =>
                    CreateAnalysisRangeState(current, clampedBoundarySeconds, currentRange.EndSeconds, telemetryData),
                _ => CreateAnalysisRangeState(current, currentRange.StartSeconds, clampedBoundarySeconds, telemetryData),
            };
        }

        if (current.PendingAnalysisRangeBoundary is not { } pendingBoundary)
        {
            return new AnalysisRangeIntentState(AnalysisRange: null, PendingAnalysisRangeBoundary: clampedBoundarySeconds);
        }

        return mode switch
        {
            AnalysisRangeBoundaryMode.Start => CreateAnalysisRangeState(current, clampedBoundarySeconds, pendingBoundary, telemetryData),
            AnalysisRangeBoundaryMode.End => CreateAnalysisRangeState(current, pendingBoundary, clampedBoundarySeconds, telemetryData),
            _ => CreateAnalysisRangeState(current, pendingBoundary, clampedBoundarySeconds, telemetryData),
        };
    }

    private static AnalysisRangeIntentState CreateAnalysisRangeState(
        AnalysisRangeIntentState current,
        double startSeconds,
        double endSeconds,
        TelemetryData telemetryData)
    {
        return TelemetryTimeRange.TryCreateClamped(
                startSeconds,
                endSeconds,
                telemetryData.Metadata.Duration,
                out var range)
            ? new AnalysisRangeIntentState(range, PendingAnalysisRangeBoundary: null)
            : current with
            {
                AnalysisRange = ClampAnalysisRange(current.AnalysisRange, telemetryData),
                PendingAnalysisRangeBoundary = null,
            };
    }

    private static Func<AnalysisSelectionState, AnalysisSelectionState>? CreateAnalysisSelectionUpdate(
        RecordedSessionEditorIntent intent,
        TelemetryData? telemetryData) => intent switch
        {
            RecordedSessionEditorIntent.SelectAnalysisRange select when telemetryData is not null =>
                current => SelectAnalysisRange(current, select.Selection),
            RecordedSessionEditorIntent.ClearAnalysisSelection => _ => CreateEmptyAnalysisSelectionState(),
            _ => null,
        };

    private static AnalysisSelectionState SelectAnalysisRange(AnalysisSelectionState current, TelemetryRangeSelection selection)
    {
        var controller = new AnalysisSelectionController(current.ActiveFront, current.ActiveRear);
        return controller.Select(selection) ? CreateAnalysisSelectionState(controller) : current;
    }

    private static AnalysisSelectionState ClearDampingRangeSelections(AnalysisSelectionState current)
    {
        var controller = new AnalysisSelectionController(current.ActiveFront, current.ActiveRear);
        return controller.ClearDampingRangeSelections() ? CreateAnalysisSelectionState(controller) : current;
    }

    private static RecordedSignalPresentationState ApplyAnalysisSelectionOverlayDefaults(
        RecordedSignalPresentationState current,
        AnalysisSelectionState selection)
    {
        if (selection.HighlightRanges.Count == 0)
        {
            return current with
            {
                ShowAnalysisSelection = false,
                ShowVelocityAnalysisSelection = false,
                ShowImuAnalysisSelection = false,
                ShowPitchRollAnalysisSelection = false,
                ShowSpeedAnalysisSelection = false,
                ShowElevationAnalysisSelection = false,
            };
        }

        return current with
        {
            ShowAnalysisSelection = true,
            ShowVelocityAnalysisSelection = false,
            ShowImuAnalysisSelection = false,
            ShowPitchRollAnalysisSelection = false,
            ShowSpeedAnalysisSelection = false,
            ShowElevationAnalysisSelection = false,
        };
    }

    private static AnalysisSelectionState CreateAnalysisSelectionState(AnalysisSelectionController controller)
    {
        return new AnalysisSelectionState(
            controller.ActiveFrontAnalysisSelection,
            controller.ActiveRearAnalysisSelection,
            []);
    }

    private static AnalysisSelectionState CreateEmptyAnalysisSelectionState()
    {
        return new AnalysisSelectionState(
            ActiveFront: null,
            ActiveRear: null,
            HighlightRanges: []);
    }

    private static TelemetryTimeRange? ClampAnalysisRange(TelemetryTimeRange? range, TelemetryData? telemetryData)
    {
        if (!range.HasValue || telemetryData is null)
        {
            return null;
        }

        return TelemetryTimeRange.TryCreateClamped(
            range.Value.StartSeconds,
            range.Value.EndSeconds,
            telemetryData.Metadata.Duration,
            out var clampedRange)
            ? clampedRange
            : null;
    }

    private sealed record Reduction(RecordedSessionEditorState Editor, int PageCount, AnalysisRangeIntentState RequestedRange);

    private sealed record AnalysisRangeIntentState(
        TelemetryTimeRange? AnalysisRange,
        double? PendingAnalysisRangeBoundary)
    {
        public static AnalysisRangeIntentState Empty { get; } = new(
            AnalysisRange: null,
            PendingAnalysisRangeBoundary: null);
    }

    private enum AnalysisRangeBoundaryMode
    {
        Nearest,
        Start,
        End,
    }

}
