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
/// cache miss is the 25 ms default. It follows recorded-session preference
/// emissions and publishes <see cref="OptionChanged"/> so the projection can
/// re-evaluate the affected sessions (the preference-&gt;projection edge).
/// </summary>
public interface IRecordedSessionProcessingOptionCache
{
    TelemetryProcessingOptions Get(Guid sessionId);

    // Emits a session id whenever its cached option changes after the first
    // preference snapshot has seeded the dictionary. The projection's first
    // sweep is gated on HydrateAsync completing instead.
    IObservable<Guid> OptionChanged { get; }

    // Loads every recorded session's option from the in-memory preferences
    // document. Idempotent; the first call establishes the baseline silently, and
    // later calls emit OptionChanged for ids whose effective option changed
    // (including ids that dropped out and revert to the default).
    Task HydrateAsync();
}

internal sealed class RecordedSessionProcessingOptionCache : IRecordedSessionProcessingOptionCache, IDisposable
{
    private const int DefaultWindow = TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds;

    private readonly ISessionPreferences sessionPreferences;
    private readonly Dictionary<Guid, int> clampedWindowsBySession = [];
    private readonly System.Threading.Lock gate = new();
    private readonly Subject<Guid> optionChanged = new();
    private readonly IDisposable preferenceSubscription;
    private bool hydrated;

    public RecordedSessionProcessingOptionCache(IAppPreferences appPreferences)
    {
        sessionPreferences = appPreferences.Session;
        preferenceSubscription = sessionPreferences.ObserveAllRecordedChanges()
            .Subscribe(change => ApplyAll(change.Value));
    }

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
        ApplyAll(await sessionPreferences.GetAllRecordedAsync());
    }

    private void ApplyAll(IReadOnlyDictionary<Guid, SessionPreferences> all)
    {
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

    public void Dispose() => preferenceSubscription.Dispose();
}
