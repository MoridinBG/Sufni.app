using Sufni.App.Sessions.Coordination;
using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal abstract record RecordedSessionEditorEffect
{
    public sealed record PersistPreferences(RecordedSessionEditorIntent Intent) : RecordedSessionEditorEffect;

    public sealed record RequestSessionInsights : RecordedSessionEditorEffect;

    public sealed record SyncMapMedia(RecordedSessionLoadedData LoadedData) : RecordedSessionEditorEffect;

    public sealed record RefreshCommands : RecordedSessionEditorEffect;


    public sealed record UpdateDirtyBaseline : RecordedSessionEditorEffect;
}

internal sealed class RecordedSessionEditorEffects : IDisposable
{
    private readonly CompositeDisposable subscriptions = [];
    private bool disposed;

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
            .Where(RecordedSessionEditorIntentReducers.IsPreferencePersistenceIntent)
            .Select(static intent => new RecordedSessionEditorEffect.PersistPreferences(intent));
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
            .Select(static _ => new RecordedSessionEditorEffect.RefreshCommands());
    }

    public static bool RequiresDraftReconciliation(SessionSnapshot previous, SessionSnapshot current) =>
        SessionDraft.From(previous) != SessionDraft.From(current);

    public static bool RequiresTelemetryReload(RecordedSessionDomainSnapshot previous, RecordedSessionDomainSnapshot current) =>
        previous.Session.HasProcessedData != current.Session.HasProcessedData ||
        previous.Session.ProcessedTelemetryRevision != current.Session.ProcessedTelemetryRevision ||
        previous.Session.ProcessingFingerprintJson != current.Session.ProcessingFingerprintJson ||
        previous.Session.DerivationWindow != current.Session.DerivationWindow ||
        previous.SelectedTrack != current.SelectedTrack ||
        previous.Session.GpsOffsetSeconds != current.Session.GpsOffsetSeconds ||
        previous.Session.Timestamp != current.Session.Timestamp ||
        previous.Session.DurationSeconds != current.Session.DurationSeconds;

    public static IObservable<RecordedSessionEditorEffect> DirtyBaselineTracking(
        IObservable<Unit> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return changes.Select(static _ => new RecordedSessionEditorEffect.UpdateDirtyBaseline());
    }

    public static IObservable<RecordedSessionEditorEffect> ExplicitAnalysisRequests(IObservable<RecordedSessionEditorIntent> intents) =>
        intents.OfType<RecordedSessionEditorIntent.RequestSessionInsights>()
            .Select(static _ => new RecordedSessionEditorEffect.RequestSessionInsights());

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        subscriptions.Dispose();
    }

}
