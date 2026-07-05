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

    public sealed record SyncMapMedia(RecordedSessionEditorState State) : RecordedSessionEditorEffect;

    public sealed record RefreshCommands(RecordedSessionEditorState State) : RecordedSessionEditorEffect;

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
}

internal sealed record RecordedSessionAnalysisEffectState(
    TravelDistributionMode TravelDistributionMode,
    VelocityAverageMode VelocityAverageMode,
    BalanceDisplacementMode BalanceDisplacementMode,
    BalanceSpeedMode BalanceSpeedMode,
    SessionInsightsTargetProfile SessionInsightsTargetProfile,
    DampingSpeedCutoffs DampingSpeedCutoffs);

internal abstract record RecordedSessionAnalysisEffectRequest
{
    public sealed record Damping(bool IncludeInsights, bool RespectSuppression)
        : RecordedSessionAnalysisEffectRequest;

    public sealed record Insights(bool RespectSuppression)
        : RecordedSessionAnalysisEffectRequest;
}
