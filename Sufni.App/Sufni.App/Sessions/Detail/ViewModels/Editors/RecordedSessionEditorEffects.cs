using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal abstract record RecordedSessionEditorEffect
{
    public sealed record PersistPreferences(RecordedSessionEditorIntent Intent) : RecordedSessionEditorEffect;

    public sealed record RequestAnalysis(RecordedSessionAnalysisEffectRequest Request) : RecordedSessionEditorEffect;

    public sealed record PublishExtensionHostState(RecordedSessionEditorState State) : RecordedSessionEditorEffect;

    public sealed record SyncMapMedia(RecordedSessionLoadedData LoadedData) : RecordedSessionEditorEffect;

    public sealed record RefreshCommands(RecordedSessionLoadedData LoadedData) : RecordedSessionEditorEffect;

    public sealed record EvaluateRecomputeStaleness(RecordedSessionEditorState State) : RecordedSessionEditorEffect;

    public sealed record UpdateDirtyBaseline(RecordedSessionEditorState State) : RecordedSessionEditorEffect;
}

internal sealed class RecordedSessionEditorEffects : IDisposable
{
    private readonly CompositeDisposable subscriptions = [];
    private bool disposed;

    public RecordedSessionEditorEffects(
        IObservable<RecordedSessionEditorEffect> effects,
        Action<RecordedSessionEditorEffect> apply)
        : this([effects], apply)
    {
    }

    public RecordedSessionEditorEffects(
        IEnumerable<IObservable<RecordedSessionEditorEffect>> effectStreams,
        Action<RecordedSessionEditorEffect> apply)
    {
        ArgumentNullException.ThrowIfNull(effectStreams);
        ArgumentNullException.ThrowIfNull(apply);

        foreach (var effectStream in effectStreams)
        {
            ArgumentNullException.ThrowIfNull(effectStream);
            subscriptions.Add(effectStream
                .Subscribe(apply));
        }
    }

    public static IObservable<RecordedSessionEditorEffect> PreferencePersistence(
        IObservable<RecordedSessionEditorIntent> intents)
    {
        ArgumentNullException.ThrowIfNull(intents);

        return intents
            .Where(IsPreferencePersistenceIntent)
            .Select(static intent => new RecordedSessionEditorEffect.PersistPreferences(intent));
    }

    public static IObservable<RecordedSessionEditorEffect> AnalysisRequests(
        IObservable<RecordedSessionEditorState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        return states
            .Select(static state => new RecordedSessionAnalysisEffectState(
                state.Intent.AnalysisRange,
                state.Intent.SelectedTravelDistributionMode,
                state.Intent.SelectedVelocityAverageMode,
                state.Intent.SelectedBalanceDisplacementMode,
                state.Intent.SelectedBalanceSpeedMode,
                state.Intent.SelectedSessionInsightsTargetProfile,
                state.Intent.DampingSpeedCutoffs))
            .Scan(
                (Previous: (RecordedSessionAnalysisEffectState?)null, Current: (RecordedSessionAnalysisEffectState?)null),
                static (current, next) => (current.Current, next))
            .Where(static pair => pair.Previous is not null && pair.Current is not null)
            .Select(static pair => CreateAnalysisRequest(pair.Previous!, pair.Current!))
            .Where(static effect => effect is not null)
            .Select(static effect => effect!);
    }

    public static IObservable<RecordedSessionEditorEffect> PageSelectionAnalysisRequests(
        IObservable<RecordedSessionEditorState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        return states
            .Select(static state => state.Intent.SelectedPageIndex)
            .Scan(
                (Previous: (int?)null, Current: (int?)null),
                static (current, next) => (current.Current, next))
            .Where(static pair => pair.Previous.HasValue &&
                pair.Current.HasValue &&
                pair.Previous.Value != pair.Current.Value)
            .Select(static _ => new RecordedSessionEditorEffect.RequestAnalysis(
                new RecordedSessionAnalysisEffectRequest.SelectedPageChanged()));
    }

    public static IObservable<RecordedSessionEditorEffect> TelemetryAnalysisRequests(
        IObservable<RecordedSessionEditorState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        return states
            .Select(static state => state.TelemetryData)
            .Scan(
                (Previous: (TelemetryData?)null, Current: (TelemetryData?)null),
                static (current, next) => (current.Current, next))
            .Where(static pair => pair.Previous != pair.Current)
            .Select(static _ => new RecordedSessionEditorEffect.RequestAnalysis(
                new RecordedSessionAnalysisEffectRequest.TelemetryChanged()));
    }

    public static IObservable<RecordedSessionEditorEffect> MapMediaSync(
        IObservable<RecordedSessionEditorState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        return states
            .Select(static state => new RecordedSessionLoadedData(
                state.Session,
                state.TelemetryData,
                state.FullTrackPoints,
                state.TrackPoints,
                state.TrackTimelineContext))
            .DistinctUntilChanged()
            .Select(static loadedData => new RecordedSessionEditorEffect.SyncMapMedia(loadedData));
    }

    public static IObservable<RecordedSessionEditorEffect> CommandRefresh(
        IObservable<RecordedSessionEditorState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        return states
            .Select(static state => new RecordedSessionLoadedData(
                state.Session,
                state.TelemetryData,
                state.FullTrackPoints,
                state.TrackPoints,
                state.TrackTimelineContext))
            .DistinctUntilChanged()
            .Select(static loadedData => new RecordedSessionEditorEffect.RefreshCommands(loadedData));
    }

    public static IObservable<RecordedSessionEditorEffect> ExplicitAnalysisRequests(
        IObservable<RecordedSessionEditorIntent> intents)
    {
        ArgumentNullException.ThrowIfNull(intents);

        return intents
            .Select(CreateExplicitAnalysisRequest)
            .Where(static effect => effect is not null)
            .Select(static effect => effect!);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        subscriptions.Dispose();
    }

    private static bool IsPreferencePersistenceIntent(RecordedSessionEditorIntent intent)
    {
        return intent is
            RecordedSessionEditorIntent.SetTravelDistributionMode or
            RecordedSessionEditorIntent.SetBalanceDisplacementMode or
            RecordedSessionEditorIntent.SetBalanceSpeedMode or
            RecordedSessionEditorIntent.SetVelocityAverageMode or
            RecordedSessionEditorIntent.SetSessionInsightsTargetProfile or
            RecordedSessionEditorIntent.SetSignalDisplayPreferences or
            RecordedSessionEditorIntent.SetSignalLayoutPreferences or
            RecordedSessionEditorIntent.SetLayoutPreferences;
    }

    private static RecordedSessionEditorEffect? CreateAnalysisRequest(
        RecordedSessionAnalysisEffectState previous,
        RecordedSessionAnalysisEffectState current)
    {
        if (previous.AnalysisRange != current.AnalysisRange)
        {
            return new RecordedSessionEditorEffect.RequestAnalysis(
                new RecordedSessionAnalysisEffectRequest.RangeChanged());
        }

        if (previous.VelocityAverageMode != current.VelocityAverageMode ||
            previous.DampingSpeedCutoffs != current.DampingSpeedCutoffs)
        {
            return new RecordedSessionEditorEffect.RequestAnalysis(
                new RecordedSessionAnalysisEffectRequest.Damping(
                    IncludeInsights: true,
                    RespectSuppression: true));
        }

        if (previous.TravelDistributionMode != current.TravelDistributionMode ||
            previous.BalanceDisplacementMode != current.BalanceDisplacementMode ||
            previous.BalanceSpeedMode != current.BalanceSpeedMode ||
            previous.SessionInsightsTargetProfile != current.SessionInsightsTargetProfile)
        {
            return new RecordedSessionEditorEffect.RequestAnalysis(
                new RecordedSessionAnalysisEffectRequest.Insights(RespectSuppression: true));
        }

        return null;
    }

    private static RecordedSessionEditorEffect? CreateExplicitAnalysisRequest(
        RecordedSessionEditorIntent intent)
    {
        return intent switch
        {
            RecordedSessionEditorIntent.RequestSessionInsights =>
                new RecordedSessionEditorEffect.RequestAnalysis(
                    new RecordedSessionAnalysisEffectRequest.Insights(RespectSuppression: false)),
            RecordedSessionEditorIntent.RequestDampingPercentages =>
                new RecordedSessionEditorEffect.RequestAnalysis(
                    new RecordedSessionAnalysisEffectRequest.Damping(
                        IncludeInsights: false,
                        RespectSuppression: false)),
            RecordedSessionEditorIntent.RefreshSelectedPageAnalysis =>
                new RecordedSessionEditorEffect.RequestAnalysis(
                    new RecordedSessionAnalysisEffectRequest.SelectedPageChanged()),
            _ => null,
        };
    }
}

internal sealed record RecordedSessionAnalysisEffectState(
    TelemetryTimeRange? AnalysisRange,
    TravelDistributionMode TravelDistributionMode,
    VelocityAverageMode VelocityAverageMode,
    BalanceDisplacementMode BalanceDisplacementMode,
    BalanceSpeedMode BalanceSpeedMode,
    SessionInsightsTargetProfile SessionInsightsTargetProfile,
    DampingSpeedCutoffs DampingSpeedCutoffs);

internal abstract record RecordedSessionAnalysisEffectRequest
{
    public sealed record RangeChanged : RecordedSessionAnalysisEffectRequest;

    public sealed record SelectedPageChanged : RecordedSessionAnalysisEffectRequest;

    public sealed record TelemetryChanged : RecordedSessionAnalysisEffectRequest;

    public sealed record Damping(bool IncludeInsights, bool RespectSuppression)
        : RecordedSessionAnalysisEffectRequest;

    public sealed record Insights(bool RespectSuppression)
        : RecordedSessionAnalysisEffectRequest;
}
