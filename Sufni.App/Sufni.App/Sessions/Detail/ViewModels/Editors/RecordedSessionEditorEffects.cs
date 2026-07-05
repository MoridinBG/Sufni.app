using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal abstract record RecordedSessionEditorEffect
{
    public sealed record PersistPreferences(RecordedSessionEditorIntent Intent) : RecordedSessionEditorEffect;

    public sealed record RequestAnalysis(RecordedSessionEditorState State) : RecordedSessionEditorEffect;

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
}
