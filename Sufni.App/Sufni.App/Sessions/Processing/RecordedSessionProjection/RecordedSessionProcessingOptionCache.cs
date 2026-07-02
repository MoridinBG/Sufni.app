using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// App-wide, synchronously-readable cache of each recorded session's clamped
/// velocity-filter processing option, backed by <see cref="ISessionPreferences"/>.
/// The recorded-session projection and domain query read it while evaluating staleness
/// so a preference-only option change is visible to the fingerprint comparison; a
/// cache miss is the 25 ms default. It re-hydrates after every remote sync apply
/// and publishes <see cref="OptionChanged"/> so the projection can re-evaluate the
/// affected sessions (the preference-&gt;projection edge).
/// </summary>
public interface IRecordedSessionProcessingOptionCache
{
    TelemetryProcessingOptions Get(Guid sessionId);

    // Emits a session id whenever its cached option changes — a local persist via
    // Set, or a re-hydrate after remote sync. NOT emitted by the first hydration:
    // the projection's first sweep is gated on HydrateAsync completing instead.
    IObservable<Guid> OptionChanged { get; }

    // Loads every recorded session's option from the in-memory preferences
    // document. Idempotent; the first call establishes the baseline silently, and
    // later calls emit OptionChanged for ids whose effective option changed
    // (including ids that dropped out and revert to the default).
    Task HydrateAsync();

    // Immediately reflects a locally persisted option so synchronous reads see it
    // without waiting for a re-hydrate; emits OptionChanged when it changed.
    void Set(Guid sessionId, TelemetryProcessingOptions option);
}

internal sealed class RecordedSessionProcessingOptionCache : IRecordedSessionProcessingOptionCache, IDisposable
{
    private const int DefaultWindow = TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds;

    private readonly ISessionPreferences sessionPreferences;
    private readonly Dictionary<Guid, int> clampedWindowsBySession = [];
    private readonly System.Threading.Lock gate = new();
    private readonly Subject<Guid> optionChanged = new();
    private readonly IDisposable syncSubscription;
    private bool hydrated;

    public RecordedSessionProcessingOptionCache(IAppPreferences appPreferences)
    {
        sessionPreferences = appPreferences.Session;

        // Re-hydrate after each remote sync apply so a synced option change for any
        // session (open or not) re-evaluates staleness through OptionChanged.
        syncSubscription = appPreferences.SyncDataApplied
            .Subscribe(_ => RehydrateAfterSync());
    }

    private void RehydrateAfterSync() => _ = HydrateAsync();

    public IObservable<Guid> OptionChanged => optionChanged.AsObservable();

    public TelemetryProcessingOptions Get(Guid sessionId)
    {
        lock (gate)
        {
            return clampedWindowsBySession.TryGetValue(sessionId, out var window)
                ? new TelemetryProcessingOptions(window)
                : TelemetryProcessingOptions.Default;
        }
    }

    public async Task HydrateAsync()
    {
        var all = await sessionPreferences.GetAllRecordedAsync();

        var changed = new List<Guid>();
        lock (gate)
        {
            var fresh = new Dictionary<Guid, int>(all.Count);
            foreach (var (sessionId, preferences) in all)
            {
                fresh[sessionId] = preferences.Processing
                    .ToTelemetryProcessingOptions()
                    .ClampedVelocityFilterWindowMilliseconds;
            }

            if (hydrated)
            {
                foreach (var (sessionId, window) in fresh)
                {
                    if (!clampedWindowsBySession.TryGetValue(sessionId, out var previous) || previous != window)
                    {
                        changed.Add(sessionId);
                    }
                }

                foreach (var (sessionId, previous) in clampedWindowsBySession)
                {
                    if (!fresh.ContainsKey(sessionId) && previous != DefaultWindow)
                    {
                        changed.Add(sessionId);
                    }
                }
            }

            clampedWindowsBySession.Clear();
            foreach (var (sessionId, window) in fresh)
            {
                clampedWindowsBySession[sessionId] = window;
            }

            hydrated = true;
        }

        foreach (var sessionId in changed)
        {
            optionChanged.OnNext(sessionId);
        }
    }

    public void Set(Guid sessionId, TelemetryProcessingOptions option)
    {
        ArgumentNullException.ThrowIfNull(option);

        var window = option.ClampedVelocityFilterWindowMilliseconds;
        bool changed;
        lock (gate)
        {
            changed = clampedWindowsBySession.TryGetValue(sessionId, out var previous)
                ? previous != window
                : window != DefaultWindow;
            clampedWindowsBySession[sessionId] = window;
        }

        if (changed)
        {
            optionChanged.OnNext(sessionId);
        }
    }

    public void Dispose() => syncSubscription.Dispose();
}
