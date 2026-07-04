using System.Reactive.Linq;
using NSubstitute;

using Sufni.App.Infrastructure;
namespace Sufni.App.Tests.TestSupport.Extensions;

public static class SessionPreferencesTestExtensions
{
    // ObserveRecorded returns null on a bare substitute; SessionDetailViewModel
    // subscribes during Loaded and would NRE. Empty observable = no preference
    // change notifications, which is the right default for tests that don't
    // exercise preference observation.
    public static ISessionPreferences WithDefaultObserveRecorded(this ISessionPreferences preferences)
    {
        preferences.ObserveRecorded(Arg.Any<Guid>()).Returns(Observable.Empty<SessionPreferences>());
        preferences.ObserveRecordedChanges(Arg.Any<Guid>())
            .Returns(Observable.Empty<PreferenceValueChange<SessionPreferences>>());
        preferences.ObserveAllRecordedChanges()
            .Returns(Observable.Empty<PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>>());
        return preferences;
    }
}
