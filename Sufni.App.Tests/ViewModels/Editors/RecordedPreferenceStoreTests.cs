using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Pages.ViewModels.Editors;
namespace Sufni.App.Tests.ViewModels.Editors;

public class RecordedPreferenceStoreTests
{
    [Fact]
    public async Task RestoreAsync_AppliesRecordedPreferencesAndEnablesPersistence()
    {
        var sessionId = Guid.NewGuid();
        var preferences = SessionPreferences.Default with
        {
            Plots = SessionPreferences.Default.Plots with { Velocity = false },
        };
        var service = Substitute.For<ISessionPreferences>();
        service.GetRecordedAsync(sessionId).Returns(preferences);
        var errors = new List<string>();
        SessionPreferences? applied = null;
        var sut = new RecordedPreferenceStore(service, () => sessionId, errors.Add);

        await sut.RestoreAsync(value => applied = value);

        Assert.Equal(preferences, applied);
        Assert.Equal(preferences, sut.Current);
        Assert.True(sut.PersistenceEnabled);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task RestoreAsync_WhenReadFails_AppliesDefaultsAndReportsError()
    {
        var sessionId = Guid.NewGuid();
        var service = Substitute.For<ISessionPreferences>();
        service.GetRecordedAsync(sessionId).Returns<Task<SessionPreferences>>(_ => throw new InvalidOperationException("bad json"));
        var errors = new List<string>();
        SessionPreferences? applied = null;
        var sut = new RecordedPreferenceStore(service, () => sessionId, errors.Add);

        await sut.RestoreAsync(value => applied = value);

        Assert.Equal(SessionPreferences.Default, applied);
        Assert.Equal(SessionPreferences.Default, sut.Current);
        Assert.True(sut.PersistenceEnabled);
        Assert.Contains(errors, error => error.Contains("bad json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PersistChangeIfEnabled_BeforeRestore_DoesNotWritePreferences()
    {
        var sessionId = Guid.NewGuid();
        var service = Substitute.For<ISessionPreferences>();
        var sut = new RecordedPreferenceStore(service, () => sessionId, _ => { });

        sut.PersistChangeIfEnabled(current => current with
        {
            Plots = current.Plots with { Velocity = false },
        });

        await service.DidNotReceive().UpdateRecordedAsync(sessionId, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
    }

    [Fact]
    public async Task PersistChangeAsync_WhenWriteFails_ReturnsFalseAndReportsError()
    {
        var sessionId = Guid.NewGuid();
        var service = Substitute.For<ISessionPreferences>();
        service.UpdateRecordedAsync(sessionId, Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns<Task>(_ => throw new InvalidOperationException("disk full"));
        var errors = new List<string>();
        var sut = new RecordedPreferenceStore(service, () => sessionId, errors.Add);

        var persisted = await sut.PersistChangeAsync(current => current with
        {
            Plots = current.Plots with { Velocity = false },
        });

        Assert.False(persisted);
        Assert.Contains(errors, error => error.Contains("disk full", StringComparison.Ordinal));
    }
}
