using System.Reactive.Subjects;
using NSubstitute;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
namespace Sufni.App.Tests.Sessions.Processing.RecordedSessionProjection;

public class RecordedSessionProcessingOptionCacheTests
{
    private readonly IAppPreferences appPreferences = Substitute.For<IAppPreferences>();
    private readonly ISessionPreferences sessionPreferences = Substitute.For<ISessionPreferences>();
    private readonly Subject<PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>> preferencesChanged = new();

    public RecordedSessionProcessingOptionCacheTests()
    {
        appPreferences.Session.Returns(sessionPreferences);
        sessionPreferences.ObserveAllRecordedChanges().Returns(preferencesChanged);
    }

    [Fact]
    public async Task HydrateAsync_SeedsOptionsWithoutPublishingChanges()
    {
        var sessionId = Guid.NewGuid();
        sessionPreferences.GetAllRecordedAsync().Returns(Preferences((sessionId, 250)));
        using var cache = new RecordedSessionProcessingOptionCache(appPreferences);
        var changed = new List<Guid>();
        using var subscription = cache.OptionChanged.Subscribe(changed.Add);

        await cache.HydrateAsync();

        Assert.Equal(250, cache.Get(sessionId).VelocityFilterWindowMilliseconds);
        Assert.Empty(changed);
    }

    [Fact]
    public async Task PreferenceEmission_UpdatesCacheAndPublishesChangedSession()
    {
        var sessionId = Guid.NewGuid();
        sessionPreferences.GetAllRecordedAsync().Returns(Preferences());
        using var cache = new RecordedSessionProcessingOptionCache(appPreferences);
        await cache.HydrateAsync();
        var changed = new List<Guid>();
        using var subscription = cache.OptionChanged.Subscribe(changed.Add);

        preferencesChanged.OnNext(new PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>(
            Preferences((sessionId, 250)),
            PreferenceChangeOrigin.LocalWrite,
            AdvancesSyncClock: true));

        Assert.Equal(250, cache.Get(sessionId).VelocityFilterWindowMilliseconds);
        Assert.Equal([sessionId], changed);
    }

    [Fact]
    public async Task PreferenceEmission_RemovesMissingSessionAndPublishesWhenItRevertsToDefault()
    {
        var sessionId = Guid.NewGuid();
        sessionPreferences.GetAllRecordedAsync().Returns(Preferences((sessionId, 250)));
        using var cache = new RecordedSessionProcessingOptionCache(appPreferences);
        await cache.HydrateAsync();
        var changed = new List<Guid>();
        using var subscription = cache.OptionChanged.Subscribe(changed.Add);

        preferencesChanged.OnNext(new PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>(
            Preferences(),
            PreferenceChangeOrigin.SyncApply,
            AdvancesSyncClock: false));

        Assert.Equal(
            TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds,
            cache.Get(sessionId).VelocityFilterWindowMilliseconds);
        Assert.Equal([sessionId], changed);
    }

    [Fact]
    public async Task PreferenceEmission_DoesNotPublishWhenEffectiveWindowIsUnchanged()
    {
        var sessionId = Guid.NewGuid();
        sessionPreferences.GetAllRecordedAsync().Returns(Preferences((sessionId, 250)));
        using var cache = new RecordedSessionProcessingOptionCache(appPreferences);
        await cache.HydrateAsync();
        var changed = new List<Guid>();
        using var subscription = cache.OptionChanged.Subscribe(changed.Add);

        preferencesChanged.OnNext(new PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>(
            Preferences((sessionId, 250)),
            PreferenceChangeOrigin.LocalWrite,
            AdvancesSyncClock: true));

        Assert.Empty(changed);
    }

    private static IReadOnlyDictionary<Guid, SessionPreferences> Preferences(params (Guid Id, int Window)[] entries)
    {
        var preferences = new Dictionary<Guid, SessionPreferences>();
        foreach (var (id, window) in entries)
        {
            preferences[id] = SessionPreferences.Default with
            {
                Processing = new SessionProcessingPreferences(window),
            };
        }

        return preferences;
    }
}
